# Pocket Plugins (initial support)

Open **POCKET PLUGINS** on the dashboard to install and manually run setup tools
using the [Pocket Plugin contract](https://github.com/openfpga-library/pocket-plugin)
shared by Pocket Sync and pupdate. Plugins can prepare game launch files, build
core-specific assets, and offer interactive setup flows.

## Using a plugin

1. Enter `owner/repository` or an HTTPS GitHub repository URL and choose
   **INSTALL PLUGIN**. The latest published release must contain `plugin.json`
   and `plugin.wasm` as individual release assets. Installation is local and does
   not execute the plugin.
2. Select the plugin and read its description, repository, release version, and
   allowed network hosts. A plugin can send data from its working copy to those
   hosts. Wildcards in the displayed host list also apply. Run only trusted plugins.
3. Scan or choose a Pocket target on the dashboard, then choose **RUN AND REVIEW**.
   ez-pocket reads and copies the entire target into a local working folder. The
   plugin receives that copy as `pocket/`, never a mount of the connected target.
4. Answer the plugin's questions on the page. Browser links appear as clickable
   links; a plugin cannot open them automatically. **CANCEL RUN** interrupts the
   operation and discards its proposed device changes.
5. Review every **ADD**, **REPLACE**, and **DELETE**. Choose **APPLY CHANGES** to
   back up originals locally and write the reviewed changes, or **DISCARD CHANGES**
   to leave the target untouched. Leaving the page discards the pending review.

Before applying, ez-pocket rechecks the target's files, directory inventory, and
root creation time, and verifies reviewed staged file hashes. Changes since the
run require a fresh run. This detects ordinary target changes; it is not a
hardware serial-number check. A failed apply attempts to restore changed files
and reports any incomplete recovery with the backup location.

## Storage and limits

- Working copies live under the app cache, in `EzPocket/plugin-runs`. Each run
  currently supports up to **64 GiB** and **200,000 files**, with local space
  required for the full copy, generated output, and backups. Large cards can take
  significant time to copy and hash. These are scan limits, not filesystem quotas
  enforced while a plugin runs; generated output can consume additional disk space.
- Installed modules and metadata live under app data, in `EzPocket/plugins`.
  Each plugin also receives a persistent private `host/` folder for caches and
  settings. Cancelling or discarding a run does not revert that private cache or
  network requests already made. **REMOVE PLUGIN** removes its installation and cache.
- Backups live under app data, in `EzPocket/backups/plugins`. The page shows the
  exact backup location after an apply attempt. `review.json` records the proposed
  changes; `originals/` contains the prior versions of replaced and deleted files.
  Keep backups needed for recovery. There is no automatic backup pruning or restore
  browser in this initial version.
- Each WebAssembly call has a five-minute timeout, a 1 GiB memory limit, a 128 MiB
  HTTP response limit, and a 16 MiB variable-store limit. Downloads are bounded:
  manifests to 64 KiB and modules to 64 MiB. HTTP is limited to the manifest's
  displayed hosts; the plugin has no general shell or host filesystem access.
- Only file changes are applied. Empty directory changes, links/junctions,
  nonportable paths, case-only renames, and replacing files with directories or
  directories with files are unsupported. Save and Memory changes are blocked;
  use Save Vault. Root `.bin` changes are blocked; use the firmware workflow.
- Root storage metadata (`System Volume Information`, `$RECYCLE.BIN`,
  `.Spotlight-V100`, `.Trashes`, `.fseventsd`, and `.TemporaryItems`) is excluded
  from the copy and change review. Plugins cannot apply changes to those locations.
- There is no automatic execution, automatic updating, or plugin catalog yet.
  To change an installed version, remove it and install the latest release again.
- Windows x64 has been exercised. The runtime packages also contain macOS/Linux
  assets, but those hosts still need platform testing. Mobile is not a supported
  plugin target.

## Validation

Service tests cover local installation, malformed releases, protocol messages,
staging isolation, cancellation/discard, target and staged-file changes, protected
workflows, rollback, and junction rejection and cleanup. Offline native runtime
tests use a small original WebAssembly fixture to exercise prompts, logging,
browser-link validation, and interrupting an executing plugin.

A compatibility smoke test on Windows x64 used the released
[`GuardianSoul/openfpga-PlayStation-plugin` v0.1.5](https://github.com/GuardianSoul/openfpga-PlayStation-plugin/releases/tag/v0.1.5)
with a synthetic core definition, CUE sheet, and four empty BIN sectors. The real
WebAssembly module asked its generation question, wrote a valid instance JSON
in staging, and applied that JSON only through the reviewed change workflow.
No game content or connected device was used.
