# Release notes

Each version has an entry headed `## <version>`, matching `<Version>` in `Directory.Build.props`. The line that
starts with `Summary:` is the plain-text summary that the in-app update check shows (at most 500 characters); the
whole entry becomes the GitHub release text. `scripts/windows/Get-ReleaseNotes.ps1` reads both, and the release
workflow refuses to start without an entry for the version. See [deployment.md](deployment.md).

No version has been published yet.

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
