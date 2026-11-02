# Pocket Plugins

Open **POCKET PLUGINS** on the dashboard to install and manually run tools using
the [Pocket Plugin contract](https://github.com/openfpga-library/pocket-plugin)
shared by Pocket Sync and pupdate. Plugins can prepare game launch files, build
assets, convert images, and offer interactive setup flows.

## Run a plugin

1. Enter `owner/repository` or its HTTPS GitHub repository URL and choose
   **INSTALL PLUGIN**. The latest release must contain `plugin.json` and
   `plugin.wasm` as individual assets. Installation downloads the plugin locally
   and does not execute it.
2. Scan or choose the Pocket target on the dashboard. Select an installed plugin
   and read its description, source, version, and allowed network hosts.
3. Choose **RUN PLUGIN** to explicitly give that plugin direct read/write access
   to the selected target. No full-card copy, hash inventory, staged change
   review, or automatic backup is made before it starts.
4. Answer its questions on the page. Browser links appear as clickable links;
   plugins cannot open them automatically. The log shows what the plugin reports.
5. Wait for completion before disconnecting the target. **CANCEL RUN** interrupts
   execution; completed writes and deletions remain. Leaving the page requests
   cancellation too. Failures do not automatically restore changed files.

**Run only trusted plugins.** They can read, replace, and delete target content,
including saves, Memories, and firmware, and send data to their allowed network
hosts. Use Save Vault separately when you want a save snapshot. The application's
own core, firmware, and restore workflows retain their review/backup behavior.

## Performance and platforms

Direct runs use the existing Extism runtime and a writable `pocket/` mount on all
supported desktop hosts. The runtime mounts only the explicitly selected target
and that plugin's private `host/` cache; it does not provide general shell access.
No custom filesystem driver or separately built overlay library is required.
The earlier 64 GiB/200,000-file working-copy limits do not apply to direct runs.
Space is needed only for the plugin's actual generated output and private cache.

A plugin may still read or generate large disc images as part of its job. The
host no longer duplicates the entire card first. Actual device performance and
plugin behavior determine run time. Windows x64 has native runtime tests; macOS
and the Linux technical preview still need platform-specific smoke testing.

## Local storage and limits

- Installations live in app data under `EzPocket/plugins`. Each plugin has its
  own persistent `host/` folder for caches and settings. **REMOVE PLUGIN** removes
  that installation and private cache. It does not revert target changes.
- New direct runs do not create `EzPocket/plugin-runs` staging folders or
  `EzPocket/backups/plugins` backups. Existing backups from older versions remain
  available at their original locations; keep any needed for recovery.
- Each WebAssembly call has a five-minute timeout, a 1 GiB memory limit, a
  128 MiB HTTP response limit, and a 16 MiB variable-store limit. Manifest
  downloads are limited to 64 KiB and modules to 64 MiB. HTTP is restricted to
  the displayed manifest hosts, including any wildcards.
- There is no automatic execution, updating, or catalog yet. To change an
  installed version, remove it and install the latest release again.

## Validation

Service tests verify direct access to the selected target, no staging or backup
creation, installation without execution, cancellation before start, missing
targets, persistence of writes after cancellation/failure, and releasing the
operation lock after a failure. Native runtime tests use an original WASI fixture
to read, write, and delete on a disposable target through the same standard
runtime used by the app, and exercise prompts, logs, links, and interruption.
No tests execute plugins against a connected device.

The legacy staging APIs remain in source but are not used by **RUN PLUGIN**.
The UI does not promise their preview, backup, or rollback guarantees.
