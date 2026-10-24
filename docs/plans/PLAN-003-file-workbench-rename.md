# PLAN-003: File Workbench / 貓檔家 rename

## Metadata

| Field | Value |
|-------|-------|
| Status | In Progress |
| Owner | Product owner |
| Assigned To | Implementation |
| Reviewer | Independent review unavailable in this session |

## Related Spec

[SPEC-003](../specs/SPEC-003-file-workbench-rename.md),
[ADR-017](../decisions/ADR-017-file-workbench-product-identity.md).

## Overview

Rename source/product/build identities while treating stored paths, ABI and
Windows identities as compatibility requirements rather than branding text.

## Implementation Phases

### Phase 1: Product and source identity

- [x] Git moves for solution, installer and native crate; update callers.
- [x] Managed namespaces/assemblies, CLI and native library names.
- [x] Localized product name/subtitle, tray and settings.

### Phase 2: Compatibility

- [x] Legacy data/environment selection without moving files.
- [x] Preserve ABI, persisted fingerprints/schemas, ownership and IPC identities.
- [x] Installer checks old/new locks and replaces only owned old payload/shortcuts.
- [x] Document retained legacy identifiers and unchanged public 0.3.0 assets.

### Phase 3: Verification

- [x] Full .NET/Rust regression, format/Clippy and workflow/script syntax checks.
- [x] Release setup/portable/source build and real package verification.
- [ ] Owner-run clean VM legacy-to-new lifecycle (AC-R04).

## Progress Log

2026-10-25: Debug and Release each passed Core 515 / GUI 27 / CLI 14 / portable
analysis 27, zero skipped with actual native/ABI/cancellation DLLs. Rust 13,
format, Clippy, actionlint and PowerShell parsing passed. Isolated WPF rendering
produced 17 screens; the Chinese header/name/subtitle was visually checked.
Release NSIS/portable/source packaging and known-answer CLI/native/image,
source-byte and missing-DLL checks passed in
`build/installer/run-6509bf77e63e49ad8674d55d2d3c2e59/`, with report
`verification/package-5ae6ab9cabfa480ea6ea76b9fff1b028/report.json`.
No installed host process was changed and no setup was installed/uninstalled.
The prepared lifecycle script accepts a legacy baseline, retains data sentinels
and checks legacy startup retargeting; its actual disposable-VM run is pending.
No independent review was available in this session. Historical 0.3.0 artifacts
and pinned third-party notice bytes remain unchanged.
