//! WASI copy-on-write mount used by the Windows preview runtime.
//! Unsupported operations fail closed; the source is never opened for writing.
use std::{any::Any, collections::{BTreeMap, BTreeSet}, fs, path::{Component, Path, PathBuf}, sync::{Arc, Mutex}};
use wasi_common::{Error, ErrorExt, dir::{OpenResult, ReaddirCursor, ReaddirEntity, WasiDir}, file::{FdFlags, FileType, Filestat, OFlags}};

struct State {
    source: PathBuf,
    stage: PathBuf,
    deleted: Mutex<BTreeSet<String>>,
    changes: Mutex<BTreeMap<String, String>>,
    copied: Mutex<u64>,
}

pub struct OverlayDir { state: Arc<State>, prefix: String }

impl OverlayDir {
    pub fn new(source: &Path, stage: &Path) -> Result<Self, Error> {
        for root in [source, stage] {
            let metadata = fs::symlink_metadata(root)?;
            #[cfg(windows)]
            { use std::os::windows::fs::MetadataExt; if metadata.file_attributes() & 0x400 != 0 { return Err(Error::not_supported()); } }
            if metadata.file_type().is_symlink() { return Err(Error::not_supported()); }
        }
        let source = source.canonicalize()?;
        let stage = stage.canonicalize()?;
        if source.starts_with(&stage) || stage.starts_with(&source) {
            return Err(Error::invalid_argument().context("source and staging must be separate"));
        }
        if fs::read_dir(&stage)?.next().is_some() {
            return Err(Error::invalid_argument().context("prototype staging must be empty"));
        }
        Ok(Self { state: Arc::new(State { source, stage, deleted: Mutex::new(BTreeSet::new()), changes: Mutex::new(BTreeMap::new()), copied: Mutex::new(0) }), prefix: String::new() })
    }

    fn relative(&self, path: &str) -> Result<String, Error> {
        // Deliberately narrower than a complete WASI filesystem in this first prototype.
        if path.contains(['\\', ':']) { return Err(Error::invalid_argument()); }
        let mut parts: Vec<String> = self.prefix.split('/').filter(|p| !p.is_empty()).map(str::to_owned).collect();
        for part in Path::new(path).components() {
            match part {
                Component::CurDir => {},
                Component::Normal(p) => {
                    let name = p.to_str().ok_or_else(Error::invalid_argument)?;
                    let stem = name.split('.').next().unwrap_or("").to_ascii_uppercase();
                    if name.ends_with(['.', ' ']) || name.chars().any(|c| c.is_control() || "<>\"|?*".contains(c)) ||
                        matches!(stem.as_str(), "CON" | "PRN" | "AUX" | "NUL") ||
                        (stem.len() == 4 && (stem.starts_with("COM") || stem.starts_with("LPT")) && stem.as_bytes()[3].is_ascii_digit()) {
                        return Err(Error::invalid_argument());
                    }
                    parts.push(name.to_owned());
                },
                _ => return Err(Error::invalid_argument().context("parent and absolute paths are unsupported")),
            }
        }
        let relative = parts.join("/");
        if parts.first().is_some_and(|name| Self::system_metadata(name)) { return Err(Error::not_supported()); }
        for root in [&self.state.source, &self.state.stage] {
            let mut checked = root.clone();
            for part in &parts {
                checked.push(part);
                match fs::symlink_metadata(&checked) {
                    Ok(m) => {
                        #[cfg(windows)]
                        { use std::os::windows::fs::MetadataExt; if m.file_attributes() & 0x400 != 0 { return Err(Error::not_supported().context("reparse point")); } }
                        if m.file_type().is_symlink() { return Err(Error::not_supported().context("symlink")); }
                    },
                    Err(e) if e.kind() == std::io::ErrorKind::NotFound => break,
                    Err(e) => return Err(e.into()),
                }
            }
        }
        Ok(relative)
    }

    fn system_metadata(name: &str) -> bool {
        ["System Volume Information", "$RECYCLE.BIN", ".Spotlight-V100", ".Trashes", ".fseventsd", ".TemporaryItems"]
            .iter().any(|entry| name.eq_ignore_ascii_case(entry))
    }

    fn visible(&self, relative: &str) -> Result<PathBuf, Error> {
        let deleted = self.state.deleted.lock().unwrap();
        let folded = relative.to_ascii_lowercase();
        if deleted.iter().any(|p| folded == p.to_ascii_lowercase() || folded.starts_with(&format!("{}/", p.to_ascii_lowercase()))) {
            return Err(Error::not_found());
        }
        let stage = self.state.stage.join(relative);
        if stage.exists() { return Ok(stage); }
        let source = self.state.source.join(relative);
        if source.exists() { Ok(source) } else { Err(Error::not_found()) }
    }

    fn parent(&self, relative: &str) -> Result<(), Error> {
        if relative.is_empty() { return Err(Error::invalid_argument()); }
        let parent = Path::new(relative).parent().unwrap().to_string_lossy();
        if !self.visible(&parent)?.is_dir() { return Err(Error::not_dir()); }
        fs::create_dir_all(self.state.stage.join(parent.as_ref()))?;
        Ok(())
    }

    fn record(&self, relative: &str, action: &str) -> Result<(), Error> {
        self.state.changes.lock().unwrap().insert(relative.to_owned(), action.to_owned());
        self.flush()
    }

    fn flush(&self) -> Result<(), Error> {
        // Kept beside the stage, never visible inside pocket/.
        let report = serde_json::json!({
            "changes": *self.state.changes.lock().unwrap(),
            "deleted": *self.state.deleted.lock().unwrap(),
            "copied_input_bytes": *self.state.copied.lock().unwrap(),
        });
        fs::write(self.state.stage.with_extension("journal.json"), serde_json::to_vec_pretty(&report).unwrap())?;
        Ok(())
    }

    fn materialize(&self, relative: &str, truncate: bool) -> Result<(), Error> {
        self.parent(relative)?;
        let stage = self.state.stage.join(relative);
        if !stage.exists() {
            if let Ok(existing) = self.visible(relative) {
                if existing.is_dir() { return Err(Error::not_supported().context("directory copy-up")); }
                if truncate { fs::File::create(&stage)?; }
                else { *self.state.copied.lock().unwrap() += fs::copy(existing, &stage)?; }
            }
        }
        Ok(())
    }

    fn physical(path: &Path) -> Result<wasi_common::sync::dir::Dir, Error> {
        Ok(wasi_common::sync::dir::Dir::from_cap_std(wasi_common::sync::Dir::open_ambient_dir(path, wasi_common::sync::ambient_authority())?))
    }

    fn names(&self, relative: &str) -> Result<Vec<String>, Error> {
        if !self.visible(relative)?.is_dir() { return Err(Error::not_dir()); }
        let mut names = BTreeSet::new();
        for root in [&self.state.source, &self.state.stage] {
            let folder = root.join(relative);
            if folder.is_dir() {
                for entry in fs::read_dir(folder)? {
                    let name = entry?.file_name().into_string().map_err(|_| Error::invalid_argument())?;
                    if relative.is_empty() && Self::system_metadata(&name) { continue; }
                    let child = if relative.is_empty() { name.clone() } else { format!("{relative}/{name}") };
                    if self.visible(&child).is_ok() { names.insert(name); }
                }
            }
        }
        Ok(names.into_iter().collect())
    }
}

#[wiggle::async_trait]
impl WasiDir for OverlayDir {
    fn as_any(&self) -> &dyn Any { self }

    async fn open_file(&self, _follow: bool, path: &str, flags: OFlags, read: bool, write: bool, fdflags: FdFlags) -> Result<OpenResult, Error> {
        let relative = self.relative(path)?;
        // Bound destructive copy-up; legitimate large replacement writes stream into staging.
        // Existing files need copying only for a partial write, not a truncate.
        let existing = self.visible(&relative).ok();
        if existing.as_ref().is_some_and(|p| p.is_dir()) {
            if flags.intersects(OFlags::CREATE | OFlags::TRUNCATE | OFlags::EXCLUSIVE) { return Err(Error::not_supported()); }
            return Ok(OpenResult::Dir(Box::new(Self { state: self.state.clone(), prefix: relative })));
        }
        if flags.contains(OFlags::DIRECTORY) { return Err(Error::not_dir()); }
        if existing.is_some() && flags.contains(OFlags::CREATE | OFlags::EXCLUSIVE) { return Err(Error::exist()); }
        let mutating = write || flags.intersects(OFlags::CREATE | OFlags::TRUNCATE) || fdflags.contains(FdFlags::APPEND);
        if !mutating {
            let physical = existing.ok_or_else(Error::not_found)?;
            let parent = Self::physical(physical.parent().unwrap())?;
            return parent.open_file(false, physical.file_name().unwrap().to_str().unwrap(), flags, read, false, fdflags).await;
        }
        if existing.is_none() && !flags.contains(OFlags::CREATE) { return Err(Error::not_found()); }
        self.materialize(&relative, flags.contains(OFlags::TRUNCATE))?;
        let parent = Self::physical(self.state.stage.join(&relative).parent().unwrap())?;
        let file = parent.open_file(false, Path::new(&relative).file_name().unwrap().to_str().unwrap(), flags, read, write, fdflags).await?;
        self.state.deleted.lock().unwrap().remove(&relative);
        self.record(&relative, "write")?;
        Ok(file)
    }

    async fn create_dir(&self, path: &str) -> Result<(), Error> {
        let relative = self.relative(path)?;
        if self.visible(&relative).is_ok() { return Err(Error::exist()); }
        self.parent(&relative)?;
        fs::create_dir(self.state.stage.join(&relative))?;
        self.state.deleted.lock().unwrap().remove(&relative);
        self.record(&relative, "mkdir")
    }

    async fn readdir(&self, cursor: ReaddirCursor) -> Result<Box<dyn Iterator<Item=Result<ReaddirEntity, Error>> + Send>, Error> {
        let mut entries = Vec::new();
        for (i, name) in self.names(&self.prefix)?.into_iter().enumerate().skip(u64::from(cursor) as usize) {
            let relative = self.relative(&name)?;
            let filetype = if self.visible(&relative)?.is_dir() { FileType::Directory } else { FileType::RegularFile };
            entries.push(Ok(ReaddirEntity { next: ((i+1) as u64).into(), inode: 0, name, filetype }));
        }
        Ok(Box::new(entries.into_iter()))
    }

    async fn unlink_file(&self, path: &str) -> Result<(), Error> {
        let relative = self.relative(path)?;
        if self.visible(&relative)?.is_dir() { return Err(Error::not_supported()); }
        let stage = self.state.stage.join(&relative);
        if stage.exists() { fs::remove_file(stage)?; }
        self.state.deleted.lock().unwrap().insert(relative.clone());
        self.record(&relative, "delete")
    }

    async fn remove_dir(&self, path: &str) -> Result<(), Error> {
        let relative = self.relative(path)?;
        if relative.is_empty() || !self.names(&relative)?.is_empty() { return Err(Error::not_supported()); }
        let stage = self.state.stage.join(&relative);
        if stage.exists() { fs::remove_dir(stage)?; }
        self.state.deleted.lock().unwrap().insert(relative.clone());
        self.record(&relative, "rmdir")
    }

    async fn get_filestat(&self) -> Result<Filestat, Error> { self.get_path_filestat(".", false).await }

    async fn get_path_filestat(&self, path: &str, _follow: bool) -> Result<Filestat, Error> {
        let relative = self.relative(path)?;
        let physical = self.visible(&relative)?;
        if relative.is_empty() { return Self::physical(&physical)?.get_filestat().await; }
        Self::physical(physical.parent().unwrap())?.get_path_filestat(physical.file_name().unwrap().to_str().unwrap(), false).await
    }

    async fn rename(&self, path: &str, dest: &dyn WasiDir, dest_path: &str) -> Result<(), Error> {
        let dest = dest.as_any().downcast_ref::<Self>().ok_or_else(Error::not_supported)?;
        if !Arc::ptr_eq(&self.state, &dest.state) { return Err(Error::not_supported()); }
        let from = self.relative(path)?;
        let to = dest.relative(dest_path)?;
        if from == to { self.visible(&from)?; return Ok(()); }
        if from.eq_ignore_ascii_case(&to) { return Err(Error::not_supported().context("case-only rename")); }
        if self.visible(&from)?.is_dir() || self.visible(&to).is_ok_and(|p| p.is_dir()) { return Err(Error::not_supported()); }
        dest.parent(&to)?;
        self.materialize(&from, false)?;
        let target = self.state.stage.join(&to);
        if target.exists() { fs::remove_file(&target)?; }
        fs::rename(self.state.stage.join(&from), target)?;
        self.state.deleted.lock().unwrap().insert(from.clone());
        self.state.deleted.lock().unwrap().remove(&to);
        self.record(&from, "delete")?;
        self.record(&to, "write")
    }
}
