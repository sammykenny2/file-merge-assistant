# Release notes

Each version has an entry headed `## <version>`, matching `<Version>` in `Directory.Build.props`. The line that
starts with `Summary:` is the plain-text summary that the in-app update check shows (at most 500 characters); the
whole entry becomes the GitHub release text. `scripts/windows/Get-ReleaseNotes.ps1` reads both, and the release
workflow refuses to start without an entry for the version. See [deployment.md](deployment.md).

No version has been published yet.

## Unreleased

- Route source registration and frozen-root checks through one replaceable path
  service, including isolated analysis sessions and image previews. Keep Windows
  defaults and saved snapshot shapes; Unix identity/operations remain unverified.

- Bound large conflict/outcome views to recycled visible controls, retaining all
  models and radio choices. Add an isolated offscreen WPF scale probe; physical
  keyboard, focus-cue and high-DPI validation remain pending in the owner's VM.
- Apply the same bound to full hub load-more groups. All loaded models remain
  accessible; regression covers final-row navigation and filtering after 10k.

- Extract read-only analysis into a plain `net10.0` leaf, preserving Windows
  workflow behavior. Add portable tests and a private owner-run Pi/macOS arm64
  verification kit. Target execution, Unix physical file identity and complete
  workflow/UI portability remain pending; see the [platform matrix](platform-support.md).

- Reject paths that cannot cross UTF-8 persistence/native boundaries losslessly;
  report unsupported NTFS names as partial scans and refuse ambiguous restore,
  retry, cache and recycle records before changing files.
- Experimental versioned C# and Czkawka analysis, frozen persistent jobs/evidence,
  read-only engine comparison and separate PNG/JPEG candidate review in WPF/CLI.
- Windows x64 local Debug installer/portable packages include the native adapter,
  dependency inventory and offline rebuild source. A retained validation kit is
  available for the owner's Sandbox/VM; clean install/upgrade/uninstall results
  are still pending.
- Managed XxHash128 remains the default. Release application builds still need
  the missing ImageSharp key and resolution of three native notice-collection
  gaps. No public release, default promotion or non-Windows GUI support is claimed.

## 0.2.1

Summary: File Merge Assistant now stays in the tray, can start when you sign in, checks for updates from the tray or Settings, and speaks English or Traditional Chinese, switched at once in Settings.

- Tray icon with a menu to open or hide the main window, check for updates and exit. Closing the main window keeps
  the app running in the tray, so scans continue.
- Settings > Startup: start File Merge Assistant when you sign in (on), keep running in the tray when the main window
  closes (on), and open the main window after a sign-in start (off). Starting at sign-in is offered by the installed
  version; a switch turned off in Windows' Startup apps stays off.
- Opening the app again brings up the window that is already running instead of an "already open" message.
- Settings > Version and updates checks, downloads and installs a new version with one button; the tray's "Check for
  updates…" does the same and asks before restarting. Exiting and installing wait while files are being moved.
- Settings > General > Display language: Auto-detect, English or Traditional Chinese. Every screen, the tray and the
  dialogs switch at once.
- A new app icon.

## 0.2.0

Summary: A new workbench that consolidates copies one small stack at a time: scan your folders, pick the copy to keep, merge in what only the others have, and move the rest to a recycle area you can restore from.

- New window with the stack-by-stack workbench: add folders, watch scans, review one stack of copies at a time, check
  the pending plan, run it, and see the history.
- Real local index (SQLite) with staged scanning that can pause and resume; code projects, regenerable folders and
  cloud-only files are recognised.
- Running the plan copies files that only other copies have into the keeper (verified by fingerprint, never
  overwriting), then moves the other copies to a recycle area on the same drive. Failed items can be retried; single
  items or whole runs can be restored; emptying the recycle area is a separate step.
- Every copy is checked on disk right before it is moved and is left alone if anything changed after you decided;
  folders are compared by identity, so a network path or junction to the keeper is never taken for a copy.
- `--demo` starts the app on the GUI flow mock's sample data.

## 0.1.0

Summary: First preview of File Merge Assistant for Windows, with a per-user setup program and an in-app update check.

- Per-user setup program (NSIS) that needs no administrator rights, with an optional `fma` command-line tool added
  to the user's PATH.
- Data (recycle bin, cache, logs, scan results) now lives in `%LOCALAPPDATA%\FileMergeAssistant` instead of the
  folder the app was started from.
- "Check for updates" reads this repository's public GitHub releases and installs a newer setup program after
  checking its size and SHA-256.
