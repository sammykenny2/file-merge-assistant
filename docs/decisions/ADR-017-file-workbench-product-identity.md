# ADR-017: File Workbench product identity and rename compatibility

## Status

Accepted, 2026-10-25. The owner selected repository `messy-file-workbench`,
English product name **File Workbench**, Traditional Chinese name **貓檔家**,
and subtitle **整理檔案的專家**, then requested the internal renames.

## Context

The public repository was renamed first. Published 0.3.0 binaries still use
File Merge Assistant identities. A blind replacement would orphan saved data,
miss running legacy executables during an upgrade, and allow both versions to
open the same index. Absolute paths in journals must remain valid.

## Decision

- Use `FileWorkbench` namespaces and assembly prefixes, including
  `FileWorkbench.Core`, `FileWorkbench.Analysis`, `FileWorkbench.Cli` and the
  `FileWorkbench` GUI. Publish the single-file CLI as `file-workbench.exe`.
- Rename the solution to `messy-file-workbench.sln`, NSIS input to
  `installer/FileWorkbench.nsi`, and native crate/directory to
  `native/file-workbench-czkawka-ffi`. The module name becomes
  `file_workbench_czkawka_ffi` with the platform's DLL/SO/dylib suffix.
- Keep ABI 1's `fma_*` exports, engine IDs, algorithm IDs, fingerprint semantics,
  wrapper version, persisted schemas and upstream pin unchanged. A product
  rename does not reinterpret saved evidence or prove engine maturity.
- Localize the visible product name, subtitle, tray and settings. The English
  subtitle is “Your file organization expert”.
- Use `FWB_*` environment variables. Keep `FMA_DATA_DIR` and the Debug-only
  `FMA_UPDATE_REPOSITORY` as lower-priority aliases.
- Fresh data uses `%LOCALAPPDATA%\FileWorkbench`. Prefer the legacy
  `%LOCALAPPDATA%\FileMergeAssistant` whenever it exists, without copying,
  moving or merging files. Explicit overrides take priority. If both directories
  exist, the existing legacy location wins until the owner explicitly selects
  another location with `FWB_DATA_DIR`.
- Retain legacy Registry/Startup keys, package ownership marker, mutex/pipe
  names, `.fma-recycle` and `.fma-partial`. These are compatibility identifiers.
  Reuse registered installation paths and preserve Startup apps opt-outs.
- Setup checks old and new executable locks before mutation. Only after
  ownership checks does it replace an old CLI and shortcuts. Uninstall retains
  application data and unrelated files.
- New release assets/manifest use File Workbench names. Do not rebuild, rename
  or replace published 0.3.0 assets. Its updater rejects repository redirects;
  the next Preview must be downloaded manually after the repository rename.

## Consequences

### Positive

The product has consistent public/source/build identities while existing indexes,
review decisions, recycle journals and startup choices remain usable.

### Negative

Some internal legacy strings intentionally remain. The change requires native
interop, GUI localization, package and old-to-new installer validation. Actual
Windows Sandbox/VM lifecycle acceptance remains owner-run.

### Neutral

This does not add ImageSharp, MSIX/Store packaging, signing, platform support or
new analysis capabilities. The local checkout directory need not be renamed.

## Alternatives Considered

### Replace every durable identity

Rejected because moving journals/recycled files or changing IPC and Windows
identities solely for branding adds avoidable data and upgrade risks.

### Rename the repository only

Already completed; the owner explicitly requested the product/source rename next.

## References

- [ADR-012](ADR-012-release-packaging.md)
- [ADR-013](ADR-013-in-app-updates.md)
- [ADR-014](ADR-014-tray-startup-and-display-language.md)
- [Deployment](../deployment.md)

---

*Decision made: 2026-10-25*
*Decision maker: Product owner*
