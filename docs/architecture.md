# System architecture

File Workbench keeps source policies, inventory, persistent jobs, review, operation planning
and restore in C# Core. Analysis is delegated through versioned contracts. WPF
remains the Windows GUI; a future cross-platform UI is a separate decision.

```text
[WPF workbench] ----+                       +-- [C# exact engine]
                   |                       |
                   +--> [C# Core] --> [IFileAnalysisEngine]
                   |       |               |
[File Workbench CLI] ---------+       |               +-- [Rust C ABI] --> [czkawka_core]
                           |
                    [SQLite / local archives]
                           |
                  [Review / plan / revalidation]
                           |
                  [PlanExecutor / recycle / restore]
```

The native engine never receives an operation plan or writes File Workbench's SQLite data.
It reads approved file inputs and streams bounded per-file evidence. The Czkawka
CLI and Krokiet are independent upstream frontends, not runtime dependencies.

## Components

| Location | Responsibility |
|----------|----------------|
| `src/Gui/` | WPF navigation, localized view models, review and operation confirmation |
| `src/Cli/` | Legacy service commands plus versioned `engines`, `analyze`, `compare-engines`, `similar-images` |
| `src/Core/Workbench/` | Shared workbench API, source registration, selection and immutable policy capture |
| `src/Core/Scanning/` | Inventory, background scheduling, frozen jobs, observation checks and generation publication |
| `src/analysis/` | Portable `net10.0` engine contracts, registry, capability/result validation and adapters |
| `native/file-workbench-czkawka-ffi/` | Pinned Rust wrapper, ABI 1, controlled lifetime and cancellation |
| `src/Core/Index/` | SQLite schema, backed-up migrations and connection helpers |
| `src/Core/Stacks/`, `Plans/` | Stack evidence, decisions, revalidation, copy/recycle execution and restore |

Core/CLI/GUI target .NET 10 Windows; GUI uses WPF and the database is SQLite.
The analysis leaf targets plain `net10.0` and has no Core, SQLite, WPF or
ImageSharp reference. Core references this leaf; the dependency never points back.
The application has no Python or Node.js runtime dependency. Rust is a build
dependency of the optional native adapter, not an end-user toolchain requirement.
There is no HTTP service or network listener in this integration.

`FileIdentityReader` preserves the existing Windows volume/file-ID observation;
Core's `PathIdentity.Identify` delegates to it. Non-Windows observations currently
have size/time only. This extraction does not make source registration, durable
workflows, operations or GUI portable. See the [platform matrix](platform-support.md)
for the remaining Windows dependencies and target-machine validation kit.

`ISourcePathService` now separates root existence, canonical paths, name/ancestor
comparison and physical identity from source registration and frozen-scope
checks. The store passes the same instance through capture, worker checks,
read-only comparison/image sessions and preview validation. Its default
`WindowsSourcePathService` keeps the existing Win32/Windows naming behavior.
Volume facts remain behind `IVolumeInfoProvider`; startup keys already use
`IStartupKeys`. These seams do not implement Unix root identities, port Windows
operations, or change persisted snapshot/identity fields. Alternative providers
must preserve those identity meanings or use an explicit migration/new-job policy.

## Exact-content flow

Source registration resolves physical roots and rejects overlap/aliases and
application-data sources. A queued job saves engine/upstream/contract/ABI,
algorithm/parameters and all relevant source policies/identities. Changes affect
new jobs; resumption resolves the original version and never silently falls back.

`SourceScanner` inventories eligible files and submits same-size candidates in
bounded cohorts, including empty files. Core validates coverage and reobserves
metadata/identity. Versioned proofs and staged generations are distinct from the
active `files.hash`; only a successful complete generation is activated.
Cancellation/partial work retains prior complete evidence and saved checkpoints.

Content evidence changes can reopen a pending decision. Plan execution rechecks
physical source contents and protection, then copies/verifies and recycles as
defined by the operation ADRs. Copy verification and historical restore do not
depend on the currently selected analysis engine or the Rust DLL being installed.

## Read-only comparison and image review

`ReadOnlyAnalysisSession` creates an independent durable index. Engine comparison
runs two such sessions sequentially, comparing observed files and normalized
group membership while keeping measurements and results separate.

`ImageSourceScanner` reuses inventory, then analyzes supported PNG/JPEG files
through a separate perceptual publication entry point. Exact scanning rejects
image snapshots; `PublishPerceptual` never activates `files.hash`. Global image
comparison creates review pairs with a defined bit distance and explicit limits.
Partial/canceled/over-limit results do not expose complete candidate lists.

`ImageReviewArchive` stores a bounded image metadata subset and manual
related/different notes under the controlled data root. It retains all referenced
pair members, validates saved scope/evidence, and rejects stale saves using the
loaded byte digest and save locks. Opening an archive needs no engine resolution.
Preview still checks current local availability, cloud flags, frozen policy and
identity before opening the image. WPF serializes background thumbnails and
bounds both axes without upscaling. Review notes never become file-operation
decisions. See [image-review.md](image-review.md).

## Native boundary and current limits

ABI 1 uses opaque IDs and caller-owned UTF-8 JSON buffers, with 2 MiB messages,
bounded upload/results and worker limits. Cancellation waits for actual worker
acknowledgement. Loaded modules remain resident while native work may exist;
same-process FFI is not crash isolation. Process-loss recovery is tested separately.

Adapter 0.2.0 pins Czkawka 12.0.2 at
`eb8b91dbb4d25dff416202674ee0de84ee4b5e5c`. Exact BLAKE3 uses its public hash API;
image evidence uses its public decoder and a documented MIT transform-hashing
adaptation. Both engines remain Experimental; managed XxHash128 remains default.
Deployment/scale acceptance and non-Windows validation are PLAN-002 Phase 5/6.

## Decisions and validation

- [ADR-015](decisions/ADR-015-pluggable-analysis-engines.md),
  [SPEC-002](specs/SPEC-002-pluggable-analysis-engines.md),
  [PLAN-002](plans/PLAN-002-pluggable-analysis-engines.md)
- [ADR-003 protection](decisions/ADR-003-protected-source-writes.md),
  [ADR-007 recycle](decisions/ADR-007-recycle-area-policy.md),
  [ADR-010 identities](decisions/ADR-010-source-identity.md)
- [Testing](testing.md), [CLI contracts](analysis-cli.md),
  [native ABI](../native/file-workbench-czkawka-ffi/ABI.md)

Updated 2026-10-24.
