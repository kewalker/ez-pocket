use extism::{Manifest, PluginBuilder, Wasm};
use std::{fs, io::{Read, Write}, path::{Path, PathBuf}, time::{Duration, Instant}};

fn sparse(file: &fs::File) -> std::io::Result<()> {
    #[cfg(windows)] {
        use std::os::windows::io::AsRawHandle;
        #[link(name = "kernel32")]
        extern "system" { fn DeviceIoControl(handle: *mut std::ffi::c_void, code: u32, input: *mut std::ffi::c_void, input_len: u32, output: *mut std::ffi::c_void, output_len: u32, returned: *mut u32, overlapped: *mut std::ffi::c_void) -> i32; }
        let mut returned = 0;
        if unsafe { DeviceIoControl(file.as_raw_handle(), 0x900c4, std::ptr::null_mut(), 0, std::ptr::null_mut(), 0, &mut returned, std::ptr::null_mut()) } == 0 { return Err(std::io::Error::last_os_error()); }
    }
    Ok(())
}

fn staged_bytes(root: &Path) -> u64 {
    fs::read_dir(root).unwrap().map(|e| { let p = e.unwrap().path(); if p.is_dir() { staged_bytes(&p) } else { fs::metadata(p).unwrap().len() } }).sum()
}

fn fixture(root: &Path) -> (PathBuf, PathBuf) {
    let source = root.join("source");
    let stage = root.join("stage");
    fs::create_dir_all(&source).unwrap();
    fs::create_dir(&stage).unwrap();
    let mut large = fs::File::create(source.join("large.bin")).unwrap();
    sparse(&large).unwrap();
    large.set_len(100 * 1024 * 1024 * 1024).unwrap();
    large.write_all(b"LARGE").unwrap();
    fs::write(source.join("replace.txt"), b"old replacement").unwrap();
    fs::write(source.join("delete.txt"), b"original deletion").unwrap();
    (source, stage)
}

fn unchanged(source: &Path) {
    assert_eq!(fs::read(source.join("replace.txt")).unwrap(), b"old replacement");
    assert_eq!(fs::read(source.join("delete.txt")).unwrap(), b"original deletion");
    assert!(!source.join("new.txt").exists());
    assert!(!source.join("renamed.txt").exists());
    assert!(!source.join("folder").exists());
    assert_eq!(fs::metadata(source.join("large.bin")).unwrap().len(), 100 * 1024 * 1024 * 1024);
    let mut first = [0; 5];
    fs::File::open(source.join("large.bin")).unwrap().read_exact(&mut first).unwrap();
    assert_eq!(&first, b"LARGE");
}

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let root = PathBuf::from(std::env::args_os().nth(1).expect("pass a NEW synthetic output directory"));
    assert!(!root.exists(), "refusing to reuse an existing directory");
    let (source, stage) = fixture(&root);
    let manifest = Manifest::new([Wasm::data(include_bytes!("probe.wat").to_vec())])
        .with_allowed_path(format!("ro:{}", source.display()), "pocket")
        .with_config_key("ezpocket.prototype.overlay.pocket", stage.to_string_lossy())
        .with_timeout(Duration::from_secs(5));
    let mut plugin = PluginBuilder::new(manifest).with_wasi(true).build()?;
    let start = Instant::now();
    assert_eq!(plugin.call::<_, String>("read_large", "")?, "OK");
    let read_ms = start.elapsed().as_millis();
    assert_eq!(staged_bytes(&stage), 0, "reading a large input must not copy it");
    plugin.call::<_, String>("partial", "")?;
    assert_eq!(fs::read(stage.join("delete.txt"))?, b"PARTinal deletion");
    plugin.call::<_, String>("mutate", "")?;
    assert_eq!(fs::read(stage.join("replace.txt"))?, b"NEW");
    assert_eq!(fs::read(stage.join("renamed.txt"))?, b"CREATED");
    assert_eq!(fs::read(stage.join("folder/nested.txt"))?, b"CREATED");
    assert!(!stage.join("delete.txt").exists());
    let listing = plugin.call::<_, Vec<u8>>("list", "")?;
    let names = dir_names(&listing);
    for expected in ["large.bin", "replace.txt", "renamed.txt", "folder"] { assert!(names.iter().any(|n| n == expected), "missing {expected}: {names:?}"); }
    for absent in ["delete.txt", "new.txt"] { assert!(!names.iter().any(|n| n == absent), "unexpected {absent}"); }
    plugin.call::<_, String>("escape", "")?;
    unchanged(&source);
    let journal: serde_json::Value = serde_json::from_slice(&fs::read(stage.with_extension("journal.json"))?)?;
    assert_eq!(journal["copied_input_bytes"], 17);
    assert!(journal["deleted"].as_array().unwrap().iter().any(|v| v == "delete.txt"));
    let bytes = staged_bytes(&stage);
    assert_eq!(bytes, 17);
    drop(plugin);
    fs::remove_dir_all(&stage)?;
    fs::create_dir(&stage)?;
    // A fresh run has a fresh overlay; cancel an executing Wasm call after it stages a write.
    let manifest = Manifest::new([Wasm::data(include_bytes!("probe.wat").to_vec())])
        .with_allowed_path(format!("ro:{}", source.display()), "pocket")
        .with_config_key("ezpocket.prototype.overlay.pocket", stage.to_string_lossy());
    let mut plugin = PluginBuilder::new(manifest).with_wasi(true).build()?;
    let cancel = plugin.cancel_handle();
    let canceller = std::thread::spawn(move || { std::thread::sleep(Duration::from_millis(300)); cancel.cancel().unwrap(); });
    assert!(plugin.call::<_, String>("cancel_loop", "").is_err());
    canceller.join().unwrap();
    assert_eq!(fs::read(stage.join("new.txt"))?, b"CREATED");
    drop(plugin);
    fs::remove_dir_all(&stage)?;
    unchanged(&source);
    let real_plugin = if let Some(module) = std::env::args_os().nth(2) { Some(smoke(&root, &source, &stage, &PathBuf::from(module))?) } else { None };
    let result = serde_json::json!({ "status": "PASS", "logical_input_bytes": 100_u64 * 1024 * 1024 * 1024, "large_file_read_ms": read_ms, "large_file_read_staged_bytes": 0, "partial_write_copied_input_bytes": 17, "final_staged_bytes": bytes, "checks": ["WASI reads", "truncate without copying original", "copy-up for partial write", "create", "rename", "delete journal", "nested directory", "merged directory listing", "path escape rejection", "discard", "cancel executing Wasm", "source unchanged"], "real_plugin": real_plugin, "root": root });
    fs::write(root.join("results.json"), serde_json::to_vec_pretty(&result)?)?;
    println!("{}", serde_json::to_string_pretty(&result)?);
    Ok(())
}

fn smoke(root: &Path, source: &Path, stage: &Path, module: &Path) -> Result<serde_json::Value, Box<dyn std::error::Error>> {
    use extism::{UserData, PTR};
    let interactive = std::env::args().any(|arg| arg == "--interactive");
    let core = source.join("Cores/GuardianSoul.PlayStation");
    let common = source.join("Assets/playstation/common/Example");
    fs::create_dir_all(&core)?;
    fs::create_dir_all(&common)?;
    fs::create_dir(stage)?;
    fs::create_dir(root.join("host"))?;
    let core_json = br#"{"core":{"metadata":{"platform_ids":["playstation"]}}}"#;
    let data_json = br#"{"data":{"data_slots":[{"id":0,"extensions":["cue"]},{"id":100,"extensions":["cue"]},{"id":101,"extensions":["bin"]}]}}"#;
    let cue = b"FILE \"Example.bin\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\n";
    fs::write(core.join("core.json"), core_json)?;
    fs::write(core.join("data.json"), data_json)?;
    fs::write(common.join("Example.cue"), cue)?;
    fs::write(common.join("Example.bin"), vec![0_u8; 2352*4])?;
    let manifest = Manifest::new([Wasm::data(fs::read(module)?)])
        .with_allowed_path(format!("ro:{}", source.display()), "pocket")
        .with_allowed_path(root.join("host").to_string_lossy().into_owned(), "host")
        .with_config_key("ezpocket.prototype.overlay.pocket", stage.to_string_lossy())
        .with_timeout(Duration::from_secs(10));
    let logs = std::sync::Arc::new(std::sync::Mutex::new(String::new()));
    let log = logs.clone();
    let mut plugin = PluginBuilder::new(manifest).with_wasi(true)
        .with_function("print_msg", [PTR], [PTR], UserData::new(()), move |current, inputs, outputs, _| {
            let message: String = current.memory_get_val(&inputs[0])?;
            if interactive { print!("{message}"); std::io::stdout().flush()?; }
            log.lock().unwrap().push_str(&message);
            outputs[0] = extism::Val::I64(0);
            Ok(())
        })
        .with_function("open_url", [PTR], [PTR], UserData::new(()), |_, _, outputs, _| { outputs[0] = extism::Val::I64(0); Ok(()) })
        .build()?;
    if interactive {
        return interact(&mut plugin, source, stage);
    }
    let start = Instant::now();
    let response = plugin.call::<_, String>("start", "")?;
    let response: serde_json::Value = serde_json::from_str(&response)?;
    assert!(response["Choice"]["choices"].as_array().unwrap().iter().any(|v| v == "Generate game files"), "unexpected menu: {response}");
    let answer = serde_json::json!({"Answer":{"name":response["Choice"]["name"],"value":"Generate game files"}});
    let reply = plugin.call::<_, String>("handle_response", answer.to_string())?;
    let generated = stage.join("Assets/playstation/GuardianSoul.PlayStation/Example.json");
    assert!(generated.exists(), "plugin generated no manifest; reply={reply}; logs={}", logs.lock().unwrap());
    let instance: serde_json::Value = serde_json::from_slice(&fs::read(&generated)?)?;
    assert_eq!(instance["instance"]["magic"], "APF_VER_1");
    assert!(!source.join("Assets/playstation/GuardianSoul.PlayStation/Example.json").exists());
    assert_eq!(fs::read(core.join("core.json"))?, core_json);
    assert_eq!(fs::read(core.join("data.json"))?, data_json);
    assert_eq!(fs::read(common.join("Example.cue"))?, cue);
    assert_eq!(fs::read(common.join("Example.bin"))?, vec![0_u8; 2352*4]);
    unchanged(source);
    let journal: serde_json::Value = serde_json::from_slice(&fs::read(stage.with_extension("journal.json"))?)?;
    assert_eq!(journal["copied_input_bytes"], 0);
    let result = serde_json::json!({"status":"PASS", "elapsed_ms":start.elapsed().as_millis(), "staged_bytes":staged_bytes(stage), "copied_input_bytes":0, "generated":instance, "log":*logs.lock().unwrap()});
    drop(plugin);
    fs::remove_dir_all(stage)?;
    Ok(result)
}

fn interact(plugin: &mut extism::Plugin, source: &Path, stage: &Path) -> Result<serde_json::Value, Box<dyn std::error::Error>> {
    println!("\nEZ-POCKET / EXPERIMENTAL PLUGIN RUNNER");
    println!("Synthetic card only. No connected Pocket is accessed.");
    println!("SOURCE: {}\nSTAGING: {}\n", source.display(), stage.display());
    let mut response = plugin.call::<_, String>("start", "")?;
    for _ in 0..100 {
        let message: serde_json::Value = serde_json::from_str(&response)?;
        if message == "Exit" { break; }
        let choice = message.get("Choice").ok_or("prototype console supports choice prompts only")?;
        let choices = choice["choices"].as_array().ok_or("missing choices")?;
        println!("\n{}", choice["query"].as_str().unwrap_or("Choose an action"));
        for (index, value) in choices.iter().enumerate() { println!("  {}. {}", index + 1, value.as_str().unwrap_or("?")); }
        print!("Choose a number, or Q to discard: ");
        std::io::stdout().flush()?;
        let mut input = String::new();
        if std::io::stdin().read_line(&mut input)? == 0 || input.trim().eq_ignore_ascii_case("q") { break; }
        let value = match input.trim().parse::<usize>().ok().and_then(|n| n.checked_sub(1)).and_then(|n| choices.get(n)) {
            Some(value) => value,
            None => { println!("Enter one of the listed numbers."); continue; }
        };
        let answer = serde_json::json!({"Answer":{"name":choice["name"], "value":value}});
        response = plugin.call::<_, String>("handle_response", answer.to_string())?;
    }
    println!("\nSTAGED OUTPUT: {} bytes", staged_bytes(stage));
    let generated = stage.join("Assets/playstation/GuardianSoul.PlayStation/Example.json");
    if generated.exists() { println!("\n{}\n{}", generated.display(), fs::read_to_string(&generated)?); }
    println!("\nThe source is unchanged. Staging is available above for inspection.");
    print!("Press Enter to discard the staged files and finish: ");
    std::io::stdout().flush()?;
    let mut input = String::new();
    std::io::stdin().read_line(&mut input)?;
    // The synthetic plugin has no outstanding native call at this point.
    fs::remove_dir_all(stage)?;
    unchanged(source);
    println!("Staged files discarded. Synthetic source remains unchanged.");
    Ok(serde_json::json!({"status":"INTERACTIVE_COMPLETE", "source_unchanged":true}))
}

fn dir_names(bytes: &[u8]) -> Vec<String> {
    let mut names = vec![];
    let mut offset = 0;
    while offset < bytes.len() {
        let len = u32::from_le_bytes(bytes[offset+16..offset+20].try_into().unwrap()) as usize;
        names.push(String::from_utf8(bytes[offset+24..offset+24+len].to_vec()).unwrap());
        offset += 24 + len;
    }
    names
}
