# Windows path encoding validation — 2026-10-24

PLAN-002 Phase 5 filesystem validation found a reproducible UTF-16→UTF-8 identity
loss on Windows 11/NTFS, .NET SDK 10.0.402 and Microsoft.Data.Sqlite 10.0.13.
Only generated files in isolated temporary directories were used.

## Reproduction

Create two real NTFS entries with distinct names: `U+D800.txt` (an unpaired high
surrogate) and `U+FFFD.txt` (the valid replacement character). Give them different
three-byte contents and equal sizes/modification times. Windows/.NET can open
each by its original UTF-16 name. Binding the first path as SQLite TEXT and
reading it back yields the second name; reading that returned path opens the
other file. System.Text.Json serialization also does not round-trip the first
path. A metadata-only comparison does not detect this identity substitution.

The regression fixture creates code units from numeric character values, so
repository/tool JSON does not need to transport malformed text itself.

## Implemented boundary

`PathText` accepts valid Unicode scalar sequences, including supplementary
characters and U+FFFD, and rejects lone surrogates/NUL. It escapes unsupported
units as literal `\uD800`/`\uDC00` diagnostics rather than replacing them.

- Source registration checks both the supplied root and resolved junction path.
- Inventory checks entry names before SQLite binding, records a read error at
  the valid parent, preserves rows under an incomplete directory walk and keeps
  the analysis partial. Both exact engines and image inventory share this walk.
- Contracts/file observations reject invalid paths before native JSON upload.
- Data/index roots, including resolved application-data aliases, are checked
  before creating the analysis index.
- Copy digests and disk snapshots refuse unrepresentable names. Execute checks
  the whole plan before its first operation/journal row; retry/restore inputs and
  journal writes check operation paths. Other-folder restore checks before moving.
- The legacy scanner skips unsupported entries with diagnostics; legacy recycle,
  cache keys/entries and scan-result JSON refuse lossy paths before mutation or
  truncating an existing output.

This is a representability check, not Unicode normalization, a ban on U+FFFD,
or a claim that a filesystem snapshot is atomic. Existing metadata/identity and
scope checks still apply. Previously substituted history cannot be inferred or
repaired automatically; this change does not rewrite historical data. No personal
index or installed application was modified during validation.

## Evidence

`PathTextTests` plus the two `PlanExecutorTests.Unsupported_*` regressions pass
**13/13**, including the actual native DLL. Independent execution also passes
13/13, zero skips. They cover valid siblings, partial inventory, escaped
diagnostics, a junction to an invalid-name target, pre-write cache/report guards,
unchanged restore destination sentinels, retained recycle contents, and rejection
before an invalid plan can create a retryable journal entry.

The complete Debug solution passes **507 Core / 25 GUI / 12 CLI**, zero skips,
with all native fixture directories configured. This completes the identified
encoding defect, not Phase 5 scale, physical USB, clean-VM or non-Windows gates.
POSIX filenames with invalid UTF-8 bytes require their own enumeration evidence;
Windows UTF-16 tests do not establish that support.
