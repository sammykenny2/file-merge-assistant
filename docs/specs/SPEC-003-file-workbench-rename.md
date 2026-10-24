# SPEC-003: File Workbench / 貓檔家 rename

## Metadata

| Field | Value |
|-------|-------|
| Status | Approved; implementation in progress |
| Owner | Product owner |
| Reviewer | Independent review unavailable in this session |

## Summary

Apply the owner's chosen product/repository names across source, GUI, CLI,
native packaging, scripts and documentation, preserving usable legacy data.
Compatibility decisions are in [ADR-017](../decisions/ADR-017-file-workbench-product-identity.md).

## Goals

- English name: File Workbench; Traditional Chinese: 貓檔家.
- Chinese subtitle: 整理檔案的專家.
- Repository: `messy-file-workbench`; assemblies/namespaces: `FileWorkbench`.

## Non-Goals

New analysis features, ImageSharp integration, signing, MSIX/Store publication,
platform maturity changes and rewriting previously published artifacts.

## Requirements

- FR-R01: Rename branded source paths with Git moves and update all build/test
  and packaging references. CLI is `file-workbench`; native module is
  `file_workbench_czkawka_ffi` with the target platform's extension.
- FR-R02: Product names, subtitle, tray and settings follow the selected language.
- FR-R03: Reuse legacy indexes, preferences and recycle journals without moving
  files or changing stored absolute paths. Explicit environment overrides win.
- FR-R04: Preserve Windows ownership/startup/IPC identities and ABI/data formats;
  installer lock checks include both old and new executable names.
- FR-R05: New packages contain matching native source/notices and new asset names.
  Old published binaries remain unchanged; manual next-version download is documented.

## Acceptance Criteria

- AC-R01: Full solution and real native interop regressions pass, with no native skips.
- AC-R02: Isolated tests prove fresh/default and legacy data selection, override
  precedence, unchanged sentinel data, and localized window title/subtitle.
- AC-R03: Release setup/portable/source build and package verifier pass using
  renamed executables, DLL, metadata and notice/source references.
- AC-R04: Owner-run disposable Windows lifecycle validates old-to-new install,
  upgrade and uninstall, including preservation of data and unrelated files.

## References

- [PLAN-003](../plans/PLAN-003-file-workbench-rename.md)
- [Deployment](../deployment.md)
