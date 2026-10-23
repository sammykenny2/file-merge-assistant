# Release notes

Each version has an entry headed `## <version>`, matching `<Version>` in `Directory.Build.props`. The line that
starts with `Summary:` is the plain-text summary that the in-app update check shows (at most 500 characters); the
whole entry becomes the GitHub release text. `scripts/windows/Get-ReleaseNotes.ps1` reads both, and the release
workflow refuses to start without an entry for the version. See [deployment.md](deployment.md).

No version has been published yet.

## 0.1.0

Summary: First preview of File Merge Assistant for Windows, with a per-user setup program and an in-app update check.

- Per-user setup program (NSIS) that needs no administrator rights, with an optional `fma` command-line tool added
  to the user's PATH.
- Data (recycle bin, cache, logs, scan results) now lives in `%LOCALAPPDATA%\FileMergeAssistant` instead of the
  folder the app was started from.
- "Check for updates" reads this repository's public GitHub releases and installs a newer setup program after
  checking its size and SHA-256.
