# Deployment

File Merge Assistant ships as a Windows x64 setup program built with NSIS. Decisions are in
[ADR-012](decisions/ADR-012-release-packaging.md) (packaging and data location) and
[ADR-013](decisions/ADR-013-in-app-updates.md) (in-app updates). Nothing has been published yet.

## What the setup program installs

- A per-user installation in `%LOCALAPPDATA%\Programs\FileMergeAssistant` that needs no administrator rights:
  `app\` (the self-contained GUI, its .NET runtime and license notices), `uninstall.exe`, and a Start menu shortcut.
- Optional: `cli\fma.exe` (the command-line tool as one self-contained file), with `cli\` added to the user's PATH,
  and a desktop shortcut. Both are off by default; an upgrade keeps the earlier choice.
- Registry: `HKCU\Software\FileMergeAssistant` (`InstallDir`, `Version`, `CliInstalled`) and the
  Apps & features entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\FileMergeAssistant`.

User data is not part of the installation. The app keeps its index, recycle bin, cache, logs and scan results in
`%LOCALAPPDATA%\FileMergeAssistant` (`FMA_DATA_DIR` overrides the location), and neither setup nor uninstall touches
it:

- `index.db` (with `index.db-wal` and `index.db-shm` while the app runs): the workbench's local index of sources,
  files, content fingerprints, scan jobs and decisions (ADR-008). It records paths, sizes, times and fingerprints, never
  file contents. Before a schema upgrade the app saves the old index as `index.db.v<N>.bak`.
- To start over, close the app and delete `index.db` and its `-wal` and `-shm` files; the sources and decisions go
  with them, and the user's own files are not affected.
- `recycle-areas\<run>\`: what a run of the plan moved out of folders on the same volume as the data folder. On
  other volumes the app uses a hidden `.fma-recycle\<run>\` folder at the volume's root, so recycling is a rename and
  never copies data. Restore from the app's history; emptying the recycle areas from the app deletes them for good.
- Starting the GUI with `--demo` uses the GUI flow mock's simulated data instead and does not open the index.

Setup and uninstall remove only what this program owns:

- `app\` and `cli\fma.exe`, when their folder carries the marker file `.installed-by-file-merge-assistant-setup`
  (setup writes it before copying anything, so an interrupted copy is still recognized), or when the registry
  recorded that folder as the installation. `cli\` itself goes only when it is then empty.
- `uninstall.exe` and the installation folder when it is empty.
- The shortcuts and registry entries, only when the registry names this folder (or nothing). Uninstalling a second
  copy in another folder therefore leaves the registered installation intact.

If the chosen folder already has an `app\` or `cli\fma.exe` that this setup did not install, setup stops before
changing anything (exit code 2 when silent).

Setup command line, besides the standard NSIS `/S` (silent) and `/D=<folder>` (must be last):

- `/CLI`: also install the command-line tool, for scripted installations.
- `/WAITPID=<pid>`: wait up to 60 seconds for that process to exit. The app passes its own process ID when it
  starts an update.
- `/RELAUNCH`: start the app when setup finishes, or start the installed app again when setup fails.

The uninstaller accepts `/S`. Setup detects a running app or `fma` by their locked executables. Interactive setup
asks the user to close them; silent setup waits up to 30 seconds and then stops with exit code 2. All checks run
before anything is changed.

## Building the setup program

Requirements: Windows x64, the .NET 10 SDK (`global.json` selects the latest installed 10.0 feature band), and
internet access the first time (NuGet/runtime packs, Cargo sources and, if needed, NSIS).
Native-by-default packaging also requires rustup, Rust 1.99.1, MSVC x64 C++ build
tools and a Windows SDK. End-user machines do not need these SDKs.

```powershell
# Debug build: no license key needed; not for release.
.\scripts\windows\Build-Installer.ps1 -Configuration Debug

# Release build: needs a Six Labors license key (see below).
$env:SixLaborsLicenseKey = '<key>'
.\scripts\windows\Build-Installer.ps1
```

The script runs the Core tests (skip with `-SkipTests`), publishes the GUI and the CLI for win-x64, collects license
notices into `app\Licenses`, stages the actual native DLL and compiles
`installer/FileMergeAssistant.nsi`. Every run uses a new retained
`build/installer/run-<id>` directory; it does not recursively clear a shared stage.
Results go to `build/` (ignored by Git):

- Release: `FileMergeAssistant-<version>-x64-setup.exe`, its `.sha256`, and
  `file-merge-assistant-update-windows.json`, the update manifest.
- Debug: `FileMergeAssistant-<version>-x64-debug-setup.exe` and its `.sha256`. No update manifest is written, so a
  Debug build can never be offered as an update. Its Apps & features name ends in "(Debug build)".
- A matching `FileMergeAssistant-<version>-x64[-debug]-portable.zip` and checksum.
  Keep its `app/` and `cli/` folders together.
- `FileMergeAssistant-<version>-czkawka-<adapter-version>[-debug]-source.zip`
  and checksum: the pinned native wrapper, vendored locked sources, notices and
  offline rebuild instructions. `app/Licenses/Rust/SOURCE.json` names the exact
  matching archive/checksum. Preserve it alongside the binaries.

`app/runtimes/win-x64/native` owns the DLL; a marked sibling `cli/` shares it.
The CLI cleanup policy still removes only its executable/marker and preserves
unrelated files. The application never searches PATH, CWD or scan sources for a
module. Static CRT packaging is checked against the DLL's actual PE imports.
Test DLLs and scan-host executables are excluded. `-WithoutNative` explicitly
builds a managed-only package without adding fallback behavior.

The builder runs `Test-AnalysisPackage.ps1` against the real self-contained CLI
with a system-only child PATH and isolated data. This is sanitized-host evidence,
not a claim that the host has no SDK or that installation passed in a clean VM.
Run publish and solution tests sequentially because they share `obj/` outputs.

### Native dependency notices

The resolved non-development Cargo graph currently contains 411 package records,
including build dependencies; it is a conservative inventory, not proof that all
are linked into the DLL. In addition to MIT, it includes rawler's LGPL-2.1 and
symphonia's MPL-2.0. The source archive preserves the full locked rebuild input.
Missing crate notices are supplemented from the exact upstream commits with
URL/SHA-256 provenance in
[third-party-notices](../native/fma-czkawka-ffi/third-party-notices/README.md).

Three pinned MIT declarations still lack standalone upstream notice text:
realfft 3.5.0, rusty-chromaprint 0.3.0 and simd_helpers 0.1.0. Native export fails
closed by default. Only internal Debug packaging retains `DeclaredOnly`,
`MISSING-NOTICE.txt` and `noticeCollectionComplete=false` and proceeds with an
explicit warning. Release packaging does not permit that flag. Notice collection
and a supplied source archive are not a legal compliance certification; resolve
the recorded gaps before public redistribution. ImageSharp's missing key remains
a separate Release gate.

The setup program is not signed yet, so Windows SmartScreen may warn before it runs (ADR-012 leaves signing open).

### NSIS

The script uses `makensis.exe` from `-NsisPath`, PATH or Program Files. Otherwise it downloads the portable NSIS
3.13 archive from SourceForge into `build/tools`, checks its SHA-256 and extracts it, so a new machine needs no NSIS
installation and no administrator rights. `-NoDownload` turns the download off.

To move to a newer NSIS: download `nsis-<version>.zip`, compare its MD5 with the one SourceForge lists for that file
(its RSS feed `https://sourceforge.net/projects/nsis/rss?path=/NSIS%203/<version>` carries `media:hash`), then update
`$NsisVersion` and `$NsisSha256` in `scripts/windows/Build-Installer.ps1`. NSIS 3.13: SHA-256
`ba63dffc4410ee89193e1cb5a41989991bd77c61068da17e3156d136b7b0b3d8`, MD5 `530e3623c077cba879ed2ae6e13be362`.

### Six Labors license key

ImageSharp 4.x checks for a Six Labors license key at build time: Debug builds only warn, Release builds fail. The
project is MIT-licensed, which the Six Labors Split License grants Apache 2.0 terms for; the owner confirms this when
requesting the key (PLAN-001 Task 0.5). Supply the key in one of these ways and never commit it:

- the `SixLaborsLicenseKey` environment variable or MSBuild property;
- `SixLaborsLicenseFile` pointing at a key file outside the repository;
- `src/Core/sixlabors.lic` (the package searches only the Core project folder; `sixlabors.lic` is ignored by Git).

The release workflow reads it from the `SIXLABORS_LICENSE_KEY` repository secret.

## Versions and release notes

The version lives only in `<Version>` in `Directory.Build.props`; the assemblies, the setup program, the update
manifest and the release tag `v<version>` all use it. Every version needs an entry in
[release-notes.md](release-notes.md) with a `Summary:` line, which becomes the in-app update summary.
`scripts/windows/Get-ReleaseNotes.ps1 -Version <x.y.z> [-Summary]` prints it.

## In-app updates

"Check for updates" (Settings > Version and updates, and "Check for updates…" in the tray menu) reads
`https://github.com/<UpdateRepository>/releases.atom`, the public release
feed (no GitHub REST API, so its per-IP quota does not apply), and picks the highest `v<major>.<minor>.<patch>` release
newer than the running version. It then reads `file-merge-assistant-update-windows.json` from that release:

```json
{
  "schemaVersion": 1,
  "platform": "windows",
  "version": "0.1.0",
  "tag": "v0.1.0",
  "summary": "Plain text, at most 500 characters",
  "assets": [{ "arch": "x64", "name": "FileMergeAssistant-0.1.0-x64-setup.exe", "size": 79339051, "sha256": "<64 hex>" }]
}
```

The manifest must match its release (tag, version, platform), and the only installer it may name is
`FileMergeAssistant-<version>-x64-setup.exe`. The installer is downloaded from the same release into
`%LOCALAPPDATA%\FileMergeAssistant\updates`; its size and SHA-256 must match the manifest, and a download that makes
no progress for 60 seconds stops. Only a copy installed by the setup program can install updates: the app checks that
it runs from `InstallDir\app`. The settings page offers one next step at a time ("Check for updates", "Download
update", "Restart and install"); downloading checks again first. Installing is refused, with the reason, while a
run, retry, restore or emptying is moving files; otherwise it opens the
installer so nothing else can change it, checks its SHA-256 again, starts it with `/S /WAITPID=<its pid> /RELAUNCH`
and exits. The tray item checks, then asks once before downloading and installing. Other copies explain why they
cannot update and link to the releases page ([ADR-014](decisions/ADR-014-tray-startup-and-display-language.md)).

## Start at sign-in and the tray

Settings > Startup has three switches, as in desktop-work-fleet: start at sign-in (on by default), keep running in
the tray when the main window closes (on), and open the main window after a sign-in start (off). Only the copy the
setup program installed can start at sign-in. It writes the value `FileMergeAssistant` under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, `"<InstallDir>\app\FileMergeAssistant.exe" --launch-at-login`:
on the first start of each installation (recorded as `StartupApplied` under `HKCU\Software\FileMergeAssistant`,
which uninstalling deletes), then only when the user changes the switch. The app never writes
`...\Explorer\StartupApproved\Run`, where Windows keeps the user's own Startup apps switch, and the settings page shows
that switch's state. Uninstalling removes the Run value. A second launch shows the running app's window through a
named pipe per user and data folder; the setup program's "is running" message tells the user to exit from the tray.

`UpdateRepository` in `Directory.Build.props` names the repository (Debug builds also accept `FMA_UPDATE_REPOSITORY`,
for testing). **The check cannot work while that repository is private**: GitHub answers the feed with 404, and the app
says the release source was not found. Make the repository public, or point `UpdateRepository` at a public
repository that holds only releases, before publishing the first release.

## Publishing a release

Not done yet; the project is too early. When it is time:

1. Resolve the native notice gaps, add the `SIXLABORS_LICENSE_KEY` repository secret
   and make the update repository public (see above). Complete clean-machine acceptance.
2. On a branch, raise `<Version>` in `Directory.Build.props`, add its entry to `docs/release-notes.md`, and merge to
   `main`.
3. Run the workflow: `gh workflow run release.yml --ref main` (or Actions > Release > Run workflow).
4. The workflow refuses a version whose tag or release already exists, builds the Release setup program on
   `windows-latest`, and creates the prerelease `v<version>` with the setup/portable
   binaries, native source archive, their checksums and the update manifest.
   A failed run publishes nothing. The workflow remains manual-only and has not
   been run for this integration.

## Testing the setup program

- `scripts/windows/Test-SetUserPath.ps1` tests the PATH helper against a scratch registry key; the real PATH is not
  touched.
- The owner will run clean install/upgrade/uninstall checks in Windows Sandbox or
  a VM. Generate a retained kit after the Debug build:

```powershell
.\scripts\windows\New-AnalysisValidationKit.ps1
```

The generated README provides exact PowerShell 5.1/7 commands. The lifecycle
script requires `-DisposableMachine` and refuses existing FMA registration,
processes, startup entries, shortcuts or user data. It checks foreign-folder
refusal, optional CLI/PATH preservation, native loading, payload replacement and
uninstall preservation of user data/foreign CLI files. It observes asynchronous
uninstall completion and retains diagnostics on failure. Baseline and current
ProductVersion/SHA-256 distinguish a real lower-version upgrade, same-version
payload replacement and reinstall. No supplied baseline means reinstall only.
Manual GUI/DPI behavior is recorded separately, never counted as script success.
The currently running host installation has not been replaced or uninstalled.
