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

User data is not part of the installation. The app keeps its recycle bin, cache, logs and scan results in
`%LOCALAPPDATA%\FileMergeAssistant` (`FMA_DATA_DIR` overrides the location), and neither setup nor uninstall touches
it. Setup and uninstall remove only what this program owns:

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
internet access the first time (NuGet packages, .NET runtime packs and, if needed, NSIS).

```powershell
# Debug build: no license key needed; not for release.
.\scripts\windows\Build-Installer.ps1 -Configuration Debug

# Release build: needs a Six Labors license key (see below).
$env:SixLaborsLicenseKey = '<key>'
.\scripts\windows\Build-Installer.ps1
```

The script runs the Core tests (skip with `-SkipTests`), publishes the GUI and the CLI for win-x64, collects license
notices into `app\Licenses`, and compiles `installer/FileMergeAssistant.nsi`. Results go to `build/` (ignored by Git):

- Release: `FileMergeAssistant-<version>-x64-setup.exe`, its `.sha256`, and
  `file-merge-assistant-update-windows.json`, the update manifest.
- Debug: `FileMergeAssistant-<version>-x64-debug-setup.exe` and its `.sha256`. No update manifest is written, so a
  Debug build can never be offered as an update. Its Apps & features name ends in "(Debug build)".

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

"Check for updates…" in the app reads `https://github.com/<UpdateRepository>/releases.atom`, the public release
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
it runs from `InstallDir\app`. After the download it asks before closing, opens the installer so nothing else can
change it, checks its SHA-256 again, starts it with `/S /WAITPID=<its pid> /RELAUNCH` and exits. Closing the update
window cancels a running download. Other copies see a link to the releases page.

`UpdateRepository` in `Directory.Build.props` names the repository (Debug builds also accept `FMA_UPDATE_REPOSITORY`,
for testing). **The check cannot work while that repository is private**: GitHub answers the feed with 404, and the app
says the release source was not found. Make the repository public, or point `UpdateRepository` at a public
repository that holds only releases, before publishing the first release.

## Publishing a release

Not done yet; the project is too early. When it is time:

1. Add the `SIXLABORS_LICENSE_KEY` repository secret and make the update repository public (see above).
2. On a branch, raise `<Version>` in `Directory.Build.props`, add its entry to `docs/release-notes.md`, and merge to
   `main`.
3. Run the workflow: `gh workflow run release.yml --ref main` (or Actions > Release > Run workflow).
4. The workflow refuses a version whose tag or release already exists, builds the Release setup program on
   `windows-latest`, and creates the prerelease `v<version>` with the setup program, its `.sha256` and the update
   manifest. A failed run publishes nothing.

## Testing the setup program

- `scripts/windows/Test-SetUserPath.ps1` tests the PATH helper against a scratch registry key; the real PATH is not
  touched.
- To test a full install on a machine, first save the user PATH, then install silently with the command-line tool,
  check the files, shortcut, registry and PATH, start the app once, uninstall silently, and compare the PATH with the
  saved value:

```powershell
$Saved = (Get-Item HKCU:\Environment).GetValue('Path', '', 'DoNotExpandEnvironmentNames')
Start-Process .\build\FileMergeAssistant-0.1.0-x64-debug-setup.exe -ArgumentList '/S', '/CLI' -Wait
# ...checks...
Start-Process "$env:LOCALAPPDATA\Programs\FileMergeAssistant\uninstall.exe" -ArgumentList '/S' -Wait
$Saved -ceq (Get-Item HKCU:\Environment).GetValue('Path', '', 'DoNotExpandEnvironmentNames')
```

The uninstaller copies itself to a temporary folder and returns at once, so wait a few seconds before checking that
the files are gone.
