# Platform evidence and remaining boundaries

Updated 2026-10-24 for PLAN-002 Phase 6. No non-Windows product support is
declared. Analysis engines remain Experimental; managed XxHash128 stays default.

## Evidence matrix

| Surface | Windows x64 | Linux arm64: owner's Pi | macOS arm64: owner's Apple Silicon Mac |
|---------|-------------|------------------------|----------------------------------------|
| Portable analysis leaf | Release contract tests passed | Source and self-contained verifier prepared; execution pending | Source and self-contained verifier prepared; execution pending |
| Native adapter | Pinned Release DLL, Rust tests and real interop tested | Vendored source; target build/test pending | Vendored source; target build/test pending |
| Known answers and image evidence | Published verifier passed; Unicode per-file fingerprints verified | Pending returned report | Pending returned report |
| Links and permissions | Symlink creation skipped in this verifier; Unix modes not applicable | Pending unprivileged symlink/mode-000 cases | Pending unprivileged symlink/mode-000 cases |
| Physical file identity | Existing volume serial/file ID/optional GUID preserved | No provider yet; size/time observations only | No provider yet; size/time observations only |
| One analysis request across volumes | Two-root known answers passed on the same volume; distinct-volume case explicitly Skipped | Optional second-volume runner prepared; actual distinct-volume run pending | Optional second-volume runner prepared; actual distinct-volume run pending |
| Persistent Core/CLI, source policies, operations/restore | Existing Debug regression suite passes; scale/clean-VM gates incomplete | Windows workflow not ported | Windows workflow not ported |
| GUI | Existing WPF; no installed host process changed | No GUI build; labwc/Wayland not validated | No GUI build |
| Packaging | Private Debug NSIS/portable; owner-run VM lifecycle pending | Private verification archive only | Private verification archive only |

The verifier may exit successfully while an optional case is explicitly Skipped.
That status is not evidence that the skipped feature works. Cross-published
ELF/Mach-O headers do not prove startup or native linking on those operating
systems. Exact OS versions, filesystems, runtime prerequisites and library
dependencies are collected on the target, not inferred from the owner's nickname.

## Extraction and Windows dependency inventory

`src/analysis/Analysis.csproj` is a `net10.0` leaf. It owns the immutable request,
result, engine registry, fingerprint contracts, managed engine, native loader and
adapter. `System.IO.Hashing` is its sole package dependency; it does not reference
Core, WPF, SQLite or ImageSharp. `FileIdentity` keeps its existing public fields
and serialized shape; the Windows identity reader moved with the contracts.
Core's existing `PathIdentity.Identify` delegates to it. No database schema or
analysis contract version changes are required by this assembly extraction.

The Windows workbench additionally accepts an `ISourcePathService` instance.
Registration, canonical/overlap checks, captured root identity, repeated worker
availability probes, isolated comparison/image sessions and image previews use
that same service. The default keeps existing Windows behavior; fake-provider
tests cover canonical/physical aliases, app-data rejection, unknown/replaced
identities, changes between probes and resumption of the original frozen job.
This implements a replaceable root-service boundary, not a Unix implementation.

| Remaining area | Current dependency | Required before a complete port |
|----------------|--------------------|---------------------------------|
| `ISourcePathService`, Windows `PathIdentity`/`RootPaths` implementation | Replaceable registration/scope service; Win32 drive/UNC/subst aliases and name policy remain the default | Implement/verify target-specific providers; operation-path port remains separate |
| Analysis observations | Win32 identity available; Unix metadata only | Stable non-Windows identity and replacement detection, including volume changes |
| Inventory and source availability | Windows cloud/reparse flags, root identities, drive notifications | Explicit filesystem/link/cloud policies and unavailable-volume tests |
| Plans/recycling/restore | Per-volume Windows paths, journal/identity revalidation | Platform-specific atomicity, permissions, crash/restore and cross-volume acceptance |
| Hosting/install/update | Registry, Run keys, Windows startup, NSIS, executable layout | Platform lifecycle/update design and tests |
| GUI and shell actions | WPF, tray, Explorer, Windows image preview | Separate UI decision plus accessibility, scrolling and system-integration validation |

No arbitrary-byte POSIX filename support is claimed. Unsupported UTF-16 paths
are rejected by the existing path-text boundary. Without a Unix physical identity
provider, same-size/same-time replacement can escape metadata observation; do not
promote this leaf into a destructive workflow on either target.

## Reproduce and return evidence

On Windows, `New-AnalysisPlatformKit.ps1` creates a unique retained directory and
ZIP with SHA-256, pinned native source/vendor, source/tests, target script, runtime
notices and self-contained verifiers. The owner runs
[the supplied instructions](analysis-platform-kit.md) locally on each machine;
no SSH credentials or remote access are needed. All fixtures are synthetic and
created in a new directory on the chosen test volume.

The script verifies shipped hashes, records the actual platform/filesystem,
builds/tests the native adapter offline with the pinned Rust toolchain, and runs
the portable verifier. Return the entire printed report directory, including
build output, loader dependencies, Cargo graph, per-check status and errors.
Use `--second-work-parent` to test both mounted volumes in one request. The
report requires different stable OS volume identities before claiming
`crossVolumeVerified`; a same-volume or unknown-identity case remains Skipped.
This read-only check does not prove cross-volume operations or physical disks.

Windows-local evidence after root-service wiring: Core 514, GUI 25 and CLI 12 tests
passed with zero skipped. Portable tests and published verifier are recorded in
[testing](testing.md#portable-analysis-leaf); the verifier's symlink case is
separately Skipped. Foreign native build/run results, USB/HDD, interactive GUI
scale and clean-VM installation remain outstanding.

## UI and release decision

[ADR-009](decisions/ADR-009-ui-technology.md) continues to select WPF. Avalonia
evaluation is deferred until target evidence and filesystem boundaries can be
assessed; no prototype, adoption or GUI migration is included here.

Earlier private kits retain their historical notice status. Current 0.3.0
packaging follows [ADR-016](decisions/ADR-016-public-native-preview.md), with
explicit standard MIT supplements for two dependencies. ImageSharp/CoenM are
omitted from the full application as well as the verifier. Automatic Windows and
arm64 Linux/macOS CI is restored; hosted CI does not replace owner filesystem
acceptance or provide a non-Windows GUI.
