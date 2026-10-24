# Testing Strategy

Source-root service wiring adds seven regression cases in
`Analysis/SourcePathServiceTests`: canonical/identity/ancestor aliases, app-data
aliases, known identity becoming unknown/replaced without metadata changes,
publication rejection followed by same-job resumption, and independent comparison
sessions preserving the injected identity. Full Windows regression after this
change passed Core 514, GUI 25, CLI 12 and portable analysis 21 with zero skips.
The default provider retains Windows rules; no Unix provider or platform gate
is inferred from fake-provider tests.

The optional [offscreen WPF scale probe](../tests/gui-scale-host/README.md) uses
10,001 in-memory stacks, 10,000 conflicts and the existing 200-row summary cap.
It asserts bounded recycled controls, retained radio choices/outcome models and
zero binding errors. [Measured evidence](research/2026-10-24-wpf-scale.md) is
separate from physical keyboard, scanning-input, high-DPI and VM acceptance.

## Portable analysis leaf

`src/analysis/Analysis.csproj` targets `net10.0` with no Windows Core/WPF/SQLite/
ImageSharp dependency. Its tests are separate from the Windows workflow suite:

```powershell
$env:FMA_NATIVE_LIBRARY_DIR = (Resolve-Path native/fma-czkawka-ffi/target/x86_64-pc-windows-msvc/release).Path
dotnet test tests/analysis-tests/Analysis.Tests.csproj --configuration Release --verbosity minimal
```

2026-10-24: 27 portable tests passed with zero skipped against the real native
Release DLL, including whole-fixture validation before writes, two-root
preflight rejection without writes and stable same-volume OS identities. The
Release verifier built without warnings. Actual single-root and two-root runs
passed; both roots were on the same Windows volume, so the distinct-volume case
was explicitly Skipped with `crossVolumeVerified: false`. An existing second
root was rejected with exit 2, no primary-root creation and unchanged sentinel.
Target Pi/macOS and genuinely different-volume runs remain pending. Four isolated
verifier processes also rejected parent escape, rooted, duplicate and empty
fixtures with exit 2, no fixture writes and unchanged sentinels.
After extraction the full Windows suite also passed Core 507, GUI 25 and CLI 12
with zero skipped. The published self-contained Windows kit verifier passed
known exact answers, per-ID Unicode/case fingerprints, PNG resize and source-byte
checks; symlink creation was explicitly Skipped for missing privilege, and Unix
modes were NotApplicable. Neither counts as a passed case.

The private [target kit](analysis-platform-kit.md) builds/tests native source and
runs the verifier on the owner's Pi/macOS machines. Those results are pending;
cross-publishing and a Windows run do not validate foreign platforms. The
[platform matrix](platform-support.md) records metadata-only Unix identity and
the remaining Windows workflow boundaries. This leaf's Release build has no
ImageSharp; it does not clear the full application's missing Release key.

File Merge Assistant uses xUnit for the .NET 10 Windows-targeted core library.
Tests live in `tests/Core.Tests/`, grouped under `Hashing/`, `Reporting/`,
`Resolution/`, `Services/`, `Stacks/`, `Updates/` and `Workbench/`. NSubstitute supplies test
doubles; file-operation fixtures create temporary directories and clean them up after each test.

`tests/Core.Tests/Localization/AppTextTests.cs` checks the language catalogs ([i18n.md](i18n.md)): both
languages of every area have the same keys and placeholders. `Infrastructure/HostPreferencesTests` and
`StartupRegistrationTests` cover `host-preferences.json` and start at sign-in against in-memory registry keys; no
test reads or writes the real Run or StartupApproved keys.

`tests/Gui.Tests/` checks the WPF workbench. `ScreenRenderingTests` walks through every screen
with the demo data on one STA thread (`WpfHost`), renders the whole window body at 1440 px wide
with `RenderTargetBitmap`, and fails on any WPF binding error. `RealIndexRenderingTests` does the
same over the real index after scanning a throwaway tree under `%TEMP%`. `ShellTests` covers the tray menu, the
three startup switches, the update states and switching the display language; it runs in the non-parallel
`Display language` collection. Both test assemblies pin the display language to `zh-TW` when they load.
`EnglishScreensTests` switches to English and walks every screen over a real index of ASCII-named folders; it fails
on any Chinese left on screen or any catalog key shown instead of its text. It skips the add-sources screen, whose
suggestions over the real store come from the real user profile.

The scanner and real-store tests (`Scanning/`, `Workbench/IndexedWorkbenchStoreTests`) build folder
trees under `%TEMP%` (`TempTree`) and an index under `FMA_DATA_DIR`. No test scans or reads the real
user profile; the first-run folder suggestions are not exercised against the real store. It needs no visible desktop, so it
also runs while the session is locked. Set `FMA_SNAPSHOT_DIR` to a folder to save each screen as
a PNG:

```powershell
$env:FMA_SNAPSHOT_DIR = "$env:TEMP\fma-shots"; dotnet test tests/Gui.Tests/Gui.Tests.csproj
```

The app writes its data under `%LOCALAPPDATA%\FileMergeAssistant` (`AppDataPaths`), or
under `FMA_DATA_DIR` when that is set. `tests/Core.Tests/TestEnvironment.cs` sets
`FMA_DATA_DIR` to a temporary folder when the test assembly loads, so no test writes to
the real data folder; `AppDataPathsTests` checks this. The update tests use a fake
`HttpMessageHandler` and never reach GitHub.

The setup program and its scripts have separate checks, described in
[deployment.md](deployment.md#testing-the-setup-program):
`scripts/windows/Test-SetUserPath.ps1` tests the PATH helper against a scratch
registry key.

## Running Tests

Run from the repository root on Windows with the .NET 10 SDK installed
(`global.json` selects the latest installed 10.0 feature band):

```powershell
dotnet test file-merge-assistant.sln --verbosity minimal
```

After dependencies have already been restored, add `--no-restore`. To collect
coverage using the existing coverlet collector:

```powershell
dotnet test tests/Core.Tests/Core.Tests.csproj --collect:"XPlat Code Coverage"
```

## Analysis Engine Baseline (2026-10-24)

The [synthetic acceptance runner](../tests/acceptance-host/README.md) independently
verifies six-source known groups/cache reuse/exclusions/long paths/source bytes
and simulated offline recovery. Both engines pass the 5k and 50k primary-file
cases; [recorded metrics](research/2026-10-24-analysis-acceptance.md) distinguish
Debug/Release profiles, model notifications, OS-cache limits and index growth.
300k and GUI/physical-device/clean-machine gates are not inferred from these runs.
The runner rejects empty/duplicate matrices and records Running/TimedOut/Failed
states under a whole-child deadline. Both finite and sustained output timeout
probes passed without touching the installed FMA process.

Slice 5b path-encoding validation passes **507 Core / 25 GUI / 12 CLI**, zero
skips. The 13 focused cases also pass independently, including actual native
requests, real lone-surrogate NTFS entries and a junction to such a target.
Valid emoji/U+FFFD siblings remain usable; unsupported inventory stays partial.
Restore, retryable journals and legacy persistence reject loss before mutation.
The [reproduction note](research/2026-10-24-windows-path-encoding.md) describes the
scope and the inability to automatically repair already-substituted history.

Slice 5a packaging changes pass **494 Core / 25 GUI / 12 CLI**, zero skips,
and **13 Rust** tests plus fmt/clippy via `Build-NativeAnalysis.ps1 -Test`.
The published self-contained CLI also passes known-answer exact/image analysis,
unchanged-source and missing-DLL diagnostics with a system-only child PATH.
Windows PowerShell 5.1 was exercised separately; this remains sanitized-host
evidence, not clean VM installation acceptance. The exported vendored source
was rebuilt/tested offline (13 passed), using a normalized absolute target path
to avoid an MSVC LNK1104 path-length failure with a long `..` output path.

`Test-AnalysisPackage.ps1` checks x64 PE imports/checksum, manifest versus loaded
engine/ABI/commit, declared notice files and explicit incomplete-notice records.
The default native exporter was exercised and correctly fails on the three
recorded notice gaps; only internal Debug application packaging opts into a
diagnostic inspection artifact. No Release key was supplied or bypassed.
Use the kit from `New-AnalysisValidationKit.ps1` for the owner's Windows
Sandbox/VM install/reinstall/upgrade/uninstall checks. Those and manual GUI/DPI
checks remain unrun here. Details are in [deployment](deployment.md).

Slice 4c full solution validation passes **461 Core / 20 GUI / 10 CLI**, zero
skips with all three native fixture directories configured. Comparison tests
cover independent indexes, normalized groups, source changes, second-run
preflight failure, frozen custom exclusions and regenerable policies, and
cancellation after worker completion or during comparison. WPF rendering and
localized completion text pass. The known-answer comparison and measurement
limits are recorded in [the evidence note](research/2026-10-24-engine-comparison.md).
This does not complete similar-image, Release, installer or non-Windows acceptance.
Independent Slice 4c review passed 15 focused Core, 10 CLI and 4 GUI tests,
zero skips, after the scope and cancellation corrections.

Slice 4d's native-image foundation passes **474 Core / 20 GUI / 10 CLI**, zero
skips, and **12 Rust** tests plus fmt/clippy. Adapter 0.2.0 retains ABI 1;
perceptual evidence is distinct from exact-content fingerprints. The synthetic
corpus records **TP 6 / FP 0 / FN 4 / TN 11** at 6 bits; crop misses and decoder
limits are detailed in [the adaptation note](research/2026-10-24-similar-images.md).
Image job persistence and candidate-review entry points remain the next slice.
The exact-index scanner explicitly rejects perceptual snapshots before any
generation or file writes. Regression coverage includes an actual supported
native image snapshot, alongside malformed-header dimension and format guards.
Independent foundation review passed 12 Rust and 21 focused Core tests, zero
skips, after resource-lifetime and exact-index boundary corrections.

Slice 4d's persistent image route adds `SimilarImageSessionTests`: native
cross-source candidates and corrupt-image errors, preservation of existing exact
proof, count/pair limits, cancellation acknowledgement, and later-cohort cloud
eviction or same-metadata replacement before backend upload. The separate
perceptual publication entry never activates `files.hash`. CLI/WPF image review
remains a subsequent slice; G4 is not complete.
Final validation passes **485 Core / 20 GUI / 10 CLI**, zero skips. Independent
review passed **44 focused cases**, zero skips, including actual DLL execution,
cloud/identity changes, final coverage, exact publication and scanner/session
regressions. The global 2000-image guard bounds pair comparison after per-source
analysis; it does not bound the sum of decoding across multiple sources.

Slice 4e / Windows x64 Debug G4 passes **493 Core / 25 GUI / 12 CLI**, zero
skips, with the real native adapter and test libraries configured. Independent
review passes **Core archive 8 / GUI review 5 / CLI 12**, zero skips. The new page
is covered by Chinese/demo and English screen walks, actual source-policy
capture, candidate preview and persistent choices, missing-native-registry
reopen, cloud/replaced-file rejection, 1×6000 / 6000×1 / 64×32 thumbnail bounds,
stale/concurrent archive saves, malformed rules and open-then-leave cancellation.
The screenshot was inspected at 1440 px. See [image-review.md](image-review.md)
for the user workflow and resource limits. This is component/integration
validation; scale, staging/installer, Release and non-Windows gates remain open.

Before PLAN-002 implementation, commit `a01ba97` passed the full solution suite:
404 Core and 16 GUI tests, SDK 10.0.402. The new shared known-answer fixture is
checked independently by `AnalysisBaselineTests` and the native adapter's Rust
tests. The focused C# baseline test passes; the native adapter has 3 passing tests.

Run Rust checks from `native/fma-czkawka-ffi/` so its pinned Rust 1.99.1 toolchain
is selected:

```powershell
cargo test --locked
cargo fmt --all -- --check
cargo clippy --locked --all-targets --no-deps -- -D warnings
```

The Slice 0 checks alone do not exercise the C ABI. The
existing ImageSharp Debug license warnings remain; this is not Release validation.
The adaptation evidence is in
[the research note](research/2026-10-24-pluggable-analysis-engines.md).

PLAN-002 Slice 1 passes the full solution suite: 416 Core and 16 GUI tests.
The workbench now delegates candidate content analysis to the managed engine;
`xxh128:` values and the executor's copy verification are unchanged. Contract
tests cover known answers, per-file coverage, cancellation, changed/missing
files, unsupported capabilities and malformed results. A fake-engine regression
checks that Core reobserves metadata instead of trusting a reported change.
Independent review passed G1 after correcting that metadata boundary. Native
interop was added in Slice 2; persisted engine snapshots and schema migration
remain pending.

PLAN-002 Slice 2 passes 421 Core and 16 GUI tests, with all native cases enabled
and zero skips; Rust passes 10 tests. The independent G2 interop review passed.
Run the complete native build/check/test path on Windows x64:

```powershell
.\scripts\windows\Build-NativeAnalysis.ps1 -Test
```

The runner builds the pinned real adapter plus two test-only DLL variants, sets
the three native test directories for its child .NET process, then restores the
environment. Native cases are optional and skipped in ordinary `dotnet test`
when `FMA_NATIVE_LIBRARY_DIR` is absent; those skips are not native validation.
Coverage includes known answers and unchanged sources, malformed requests and
DLLs, ABI mismatch, bounded output, short-buffer retry, repeated disposal and
cancellation while uploading inputs or while a worker is blocked. Cancellation
completion requires actual worker exit. Process-level recovery after termination
is a separate Phase 3 test, not covered by these results. No Release application
validation has passed: a valid ImageSharp license has not been obtained.

Slice 3a adds an archived schema-3 SQL fixture (from `9ba0275`) and a migration
test that checks the pre-upgrade backup, legacy fingerprint attribution, pending
decisions and completed/recycled journal fields. Nine focused snapshot/migration
tests also cover fixed queue-time scope after settings changes and reopening,
missing original engine versions, new IDs on restart, malformed/legacy snapshots,
replacement root identity, volume changes before a checkpoint and cancellation
acknowledgement, including source changes between initial probes and cancellation
during a blocked preflight. The full suite passes 430 Core and 16 GUI tests,
zero skips, and independent review passed after correcting both races. This does
not yet validate staged publication of a new analysis
generation or process-loss recovery; those are the remaining Phase 3 slices.

Slice 3b passes the full suite: 443 Core and 16 GUI tests, zero skips. The evidence,
plan-compatibility and schema migration group has 14 tests with the real native
DLL enabled. The 450-file interruption fixture saves 200 inactive SHA-256 results,
preserves all 450 active XxHash128 results, then reobserves/reuses those 200 and
hashes the remaining 250 on resume. A separate test replaces a prior-cohort file
with identical size/time but another identity before publication; the new
generation stays inactive. Coverage also includes pause, failed/malformed final
results, true zero-byte files, frozen comparison exclusions, algorithm-specific
cache reuse and retained computation provenance. Both archived schema 3 and
schema 4 migration fixtures check backups and retained history/checkpoints.

Changing analysis evidence reopens pending plans even when the filesystem
metadata digest is unchanged. New evidence and physical metadata changes have
separate GUI explanations. Plan tests execute after fresh confirmation and
restore with only the managed engine registered; the native module can still be
resident in that test process. A cold-start missing-DLL restore test and process
termination/recovery remain Slice 3c requirements, so G3 is not complete yet.

Slice 3c completes the Windows x64 G3 scope: 444 Core and 16 GUI tests pass,
zero skips, with independent review and a separate 4-test recovery/compatibility
run. [The test-only host](../tests/scan-host/README.md) is built automatically.
The parent terminates only its own child after a real-DLL 200-file checkpoint
and entry into the next native upload. Reopening preserves the original snapshot,
450 active legacy fingerprints and saved inventory; the missing engine waits,
and the original version resumes the same job as generation 2. Checkpoint events
bypass the UI progress throttle explicitly rather than relying on wall-clock time.

The native history/restore case now starts a fresh process with an absent native
installation directory and no native-test environment variables. The child
asserts zero FMA native modules before and after successful restoration. This is
cold-start recovery evidence, not a claim that native process aborts are caught.
All process markers and source files are isolated fixtures; test hosts and native
fault fixtures must not be included in application packages.

Slice 4a passes 446 Core and 19 GUI tests, zero skips with the native environment
enabled. Core regressions check persisted future-job selection and corrupt
settings without hidden fallback. The three new WPF tests cover unavailable
options, locale changes, preserved queued snapshots, real Rust selection and
Blake3 results, and demo-mode selection on the UI thread. The independent
focused run passes Core 10 / GUI 3. The settings view was rendered and visually
checked at 1440 px; `FMA_SNAPSHOT_DIR` saves `analysis-settings-zh.png`.

The real store saves selection asynchronously and serializes its database/memory
update; demo selection stays on the caller's UI thread. Original job versions,
algorithms and frozen scope remain visible after changing the new-job choice.
These tests cover exact-content UI only; CLI and similar-image acceptance are
separate Phase 4 work.

Slice 4b adds `tests/cli-tests/` to the solution and a shared read-only analysis
session in Core. Full results are Core 453 / GUI 19 / CLI 8, zero skips with the
native environment enabled; independent focused results are Core 9 / CLI 8.
The new cases verify the known-answer oracle across two registered sources for
both managed algorithms and real Rust, unchanged source bytes, retained unique
files, missing-engine rejection, cancellation acknowledgement and canonical
data-folder exclusions through a junction with a newly created leaf. Migration
fixtures create their own parent directory and do not depend on test ordering.

CLI tests assert versioned metadata/JSON, nonzero diagnostics instead of empty
success, explicit native selection, and unchanged legacy `scan` defaults with
lazy optional-library resolution. A real `fma.dll` process smoke test also parsed
its stdout as JSON. [analysis-cli.md](analysis-cli.md) describes the command and
process-metric limits. Engine comparison and similar-image acceptance remain
pending.

## CI Pause

GitHub Actions CI is temporarily disabled for this repository. The `CI` workflow
is disabled in GitHub, and `.github/workflows/ci.yaml` retains only the manual
`workflow_dispatch` event. Pushes and pull requests do not automatically trigger
CI. Existing workflow history is preserved.

To permit manual runs again, explicitly re-enable the workflow:

```powershell
gh workflow enable ci.yaml --repo sammykenny2/file-merge-assistant
```

Automatic CI also requires restoring the `push` and `pull_request` events in the
workflow file. Re-enable it only when requested. Its jobs validate required files
and YAML; they do not run this .NET test suite.

## Test Baseline

Checked on 2026-10-23 with SDK 10.0.402 and `net10.0-windows`, with
`TreatWarningsAsErrors` on (set in `Directory.Build.props`) and NuGet audit at its
default:

- `dotnet build file-merge-assistant.sln` succeeds for the Debug configuration.
- `dotnet test tests/Core.Tests/Core.Tests.csproj` passes all 149 tests; VSTest
  reports under about a second of test time. (Later additions are listed under
  "Changes since this baseline".)
- `dotnet list file-merge-assistant.sln package --vulnerable --include-transitive`
  reports no vulnerable packages.
- Smoke check: `fma --help` and `fma scan` on a small folder succeed, and the
  GUI opens its main window and stays responsive.

The same build and tests also passed on SDK 8.0.426 with `net8.0-windows`
immediately before the upgrade.

With this SDK, NuGet audit also checks transitive packages, not only direct ones.
Because `TreatWarningsAsErrors` is on, a new advisory against any transitive
package fails restore and therefore the build. Fix it by updating or pinning the
package; record any deliberate suppression here.

To guard against future hangs, add a bounded timeout when diagnosing:

```powershell
dotnet test tests/Core.Tests/Core.Tests.csproj --blame-hang-timeout 60s --blame-hang-dump-type none
```

When a test hangs, VSTest writes a sequence attachment under
`tests/Core.Tests/TestResults/` that names the test that was running.

### Fixes That Established This Baseline

- CA1716: `IWatchService.Start()`/`Stop()` are now `StartWatching()`/`StopWatching()`.
- CA1859: `PreviewStatisticsService.BuildDirectoryBreakdown()` returns `List<T>`.
- CA1305: `ScannedFileViewModel.DateTaken` formats with the invariant culture.
  This blocked the solution build, not the test build.
- `EmptyDirectoryService.Cleanup(dryRun: true)` never returned when the tree held
  an empty directory, which hung `Cleanup_DryRun_Does_Not_Delete`. Cleanup now
  makes one deepest-first pass. When every delete succeeds, a dry run reports
  the same count as a real cleanup; a directory that cannot be deleted (in use,
  read-only) is still counted by a dry run.
- Independent review found that cleanup followed junctions and directory
  symlinks, so it could delete empty directories outside the root (this predates
  the dry-run fix). Cleanup now skips reparse points, so it neither follows nor
  deletes them, and a directory that contains one is not treated as empty.
  Inaccessible subdirectories are skipped instead of aborting the whole cleanup.
  Regression tests cover nested and mixed trees, an empty root, and a junction
  that points outside the root.
- NuGet audit errors NU1902/NU1903: `SixLabors.ImageSharp` moved from 2.1.13 to
  4.1.3, which fixes GHSA-gwg2-r3hj-4w44, GHSA-wmxv-xphr-5c9g,
  GHSA-j3p4-wp97-rph4, GHSA-j9gm-c75j-xc9q and GHSA-jjfr-hcj7-qf5w.
- .NET 10 upgrade: all projects target `net10.0-windows`; `global.json` requires
  the 10.0 SDK. Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Hosting
  and System.IO.Hashing moved to 10.0.13, and Serilog to 4.2.0. Serilog
  initially moved to 4.2.0 because Serilog.Extensions.Logging 10.0.0 required it.
  That package was later removed because nothing used it: the code logs through
  `Serilog.ILogger` directly. Serilog stays at 4.2.0.
  The upgrade also removed the transitive vulnerable packages that the .NET 8
  build pulled in. System.Text.Json 8.0.0 left Gui with the
  Microsoft.Extensions.Hosting 10.0.13 update. System.Net.Http and
  System.Text.RegularExpressions 4.3.0 left Core.Tests because the .NET 10 SDK
  prunes packages that the shared framework already provides; restoring with
  `-p:RestoreEnablePackagePruning=false` brings them back as NU1903 errors.

### Known Remaining Issues

- **Release builds fail without a Six Labors license key.** ImageSharp 4.x is
  under the Six Labors Split License, which grants Apache 2.0 terms to software
  under an open source license; this project is MIT-licensed, which the owner
  should confirm when requesting the key. The package validates a key at build
  time: Debug builds only warn. Supply the key as the `SixLaborsLicenseKey`
  property or environment variable, point `SixLaborsLicenseFile` at a key file
  outside the repository, or place `sixlabors.lic` under `src/Core/` (the package
  only searches the Core project folder). `sixlabors.lic` is ignored by Git.
  Obtaining the key is a prerequisite for release packaging (PLAN-001 Phase 7).
- **CoenM.ImageSharp.ImageHash 1.3.6 was built against ImageSharp 2.x** and
  declares an open `>= 2.1.3` range. The one API this project uses,
  `AverageHash.Hash`, is covered by `PerceptualHashAlgorithmTests` and gave the
  same hashes under 2.1.13 and 4.1.3 in review. Rerun those tests whenever
  ImageSharp is updated.
- Test tooling was kept at its existing versions (Microsoft.NET.Test.Sdk 17.8.0,
  xunit 2.6.2, xunit.runner.visualstudio 2.5.4, NSubstitute 5.1.0, coverlet 6.0.0).
  They run on .NET 10; updating them is separate work.

### Changes Since This Baseline

- 2026-10-23, setup program and update check (PLAN-001 Tasks 2.1, 7.1, 7.4 to 7.6):
  - Behavior change: the recycle bin, scan cache and log file moved from
    `fma-results\` under the working directory to `%LOCALAPPDATA%\FileMergeAssistant`
    (`recycle-bin`, `cache`, `logs`); the GUI writes scan results to its `results`
    folder. Files already in an old `fma-results\.recycle-bin` stay there and are no
    longer listed; restore them with an older build or move them by hand. The CLI's
    `--output` default (`./fma-results/`) is unchanged.
  - New tests: `AppDataPathsTests`, `UpdateFeedTests` and `UpdateServiceTests`;
    `Test-SetUserPath.ps1` for the installer's PATH helper. The suite has 214 tests.
  - Setup program, checked on 2026-10-23 with the Debug build of 0.1.0 on the
    development machine:
    - Silent install with `/CLI`: files, owner markers, Start menu shortcut, registry and
      Apps & features entry all correct; PATH gained exactly `;<install>\cli` with its
      REG_EXPAND_SZ kind kept; the installed `fma --version` reports 0.1.0.
    - The installed app starts and stays responsive.
    - Setup while the app runs: exit code 2 after 30 seconds, with no file changed.
    - Update hand-off (`/S /WAITPID=<pid> /RELAUNCH`): setup waited for the app,
      upgraded it, kept the CLI without duplicating the PATH entry, and restarted the app.
    - Silent uninstall: folder, shortcut and registry entries removed, and PATH
      byte-identical to the value saved before the test.
    - The GUI writes no log at startup, so the data folder appears only once the app logs
      something; a CLI scan wrote its log to `%LOCALAPPDATA%\FileMergeAssistant\logs` and
      created nothing in the working directory.
    - `https://github.com/sammykenny2/file-merge-assistant/releases.atom` answers 404
      while the repository is private, so the in-app check reports that the release source
      was not found. That is the only update path verifiable today.

- 2026-10-23, workbench screens on demo data (PLAN-001, see the 2026-10-23 progress log entry):
  - New tests: `StackReviewTests`, `PlanBuilderTests`, `StackTriageTests` and
    `DemoWorkbenchStoreTests` in Core.Tests (270 tests in total), and the new Gui.Tests project
    with 2 tests. All 272 pass.
- 2026-10-23, real index (PLAN-001 Phases 2 to 4, read-only):
  - New tests: `StackDetectorTests` (ground-truth trees: project copies under other names, subset
    folders, wrappers and containers, boilerplate inside code projects), `SourceScannerTests`,
    `IndexedWorkbenchStoreTests` and `RealIndexRenderingTests`. Core.Tests has 304 tests, Gui.Tests 3;
    all 307 pass.
- 2026-10-23, running the plan (PLAN-001 Phase 5):
  - `PlanExecutorTests` move and copy real files inside temp trees, with the recycle area under the test data
    folder: copy and verify, never overwrite, a stack's recycling held back when its copy fails, a protected
    copy refused, a locked file left behind and moved on retry, restore, a restore conflict settled by
    renaming, and emptying. Store tests run, restore and empty end to end, and the real-index GUI test runs
    the plan from the plan screen. Core.Tests has 319 tests, Gui.Tests 3; all 322 pass.
- 2026-10-23, fixes from the Phase 5 reviews:
  - `PlanExecutorTests` add: every item journalled as running with its paths before it acts; a partly moved
    folder never emptied and put back file by file; retry moving only files unchanged since the run; a copy
    retry refused when its source was edited; restore leaving a copied file the user edited; emptying a
    recycled folder that holds a junction without touching the junction's target; a folder gone before its
    move failing without moving anything; replace and its restore.
  - `IndexedWorkbenchStoreTests` add: a copy whose files changed after the decision going back for another
    look (and staying out of the plan after a restart); a keeper emptied after the decision stopping the whole
    stack; a copy edited right before the run skipped although the index is stale; convergence only when
    every item finished (after a retry); a pending decision with an offline keeper waiting without errors;
    another disk under the same letter not scanned; a folder added while another source is offline; a junction
    to a source refused as a second source; the
    app's own folder, recycle areas and partial files never scanned; a crashed run settled from the disk on
    the next start; a copy not undone while its source is still recycled.
  - Core.Tests has 338 tests, Gui.Tests 3; all 341 pass.
- 2026-10-23, fixes from the second Phase 5 review:
  - `PlanExecutorTests` add: a copy that is the keeper under another name (a junction, and `\\localhost\C$`
    when the administrative share is reachable) is never recycled; a copy holding the app's own folder is never
    recycled; undoing a copy waits until its source is back even when another run recycled it; emptying keeps
    what it cannot reach and it stays restorable; unchecked and cancelled items are recorded as skipped; a
    locked single file moves on retry only while unchanged; a leftover partial file does not block a copy.
  - `IndexedWorkbenchStoreTests` add: a network path to a source is not added as another source; changes the
    scan does not list (`.git`, `node_modules`) keep a copy from being recycled; a retry is refused once the
    keeper is gone; unchecked items keep the stack from counting as done; an interrupted item on an unplugged
    drive waits for it.
  - `SourceScannerTests` add: generic folder names count as regenerable only next to a project marker.
  - With the identity check switched off, the junction and app-folder tests fail and the UNC test is still
    stopped by the after-move backstop; with the snapshot check switched off, the `.git` test fails.
  - Core.Tests has 352 tests, Gui.Tests 3; all 355 pass, three runs in a row.
- 2026-10-23, fixes from the third Phase 5 review:
  - `PlanExecutorTests` add: undoing a copy is refused when another file now sits where its source was; a probe
    shows up under every name of its folder (a junction) and is gone afterwards; a cloned drive is not the same
    volume even with the same serial and ids; a snapshot taken while the copy changes is not a baseline.
  - `IndexedWorkbenchStoreTests` add: a retry is refused once the keeper is empty; a change right after deciding
    keeps the copy even before the background snapshot is done.
  - With the source-stamp check or the snapshot time cut-off switched off, their tests fail.
  - Not testable here: a NAS that reports a different volume serial per share name (the probe covers it), and a
    sector-cloned drive (the volume GUID covers it; the comparison itself is unit-tested).
  - Core.Tests has 358 tests, Gui.Tests 3; all 361 pass, three runs in a row.
- 2026-10-23, fourth Phase 5 review (OK to merge):
  - The reviewer re-ran its scenarios (UNC alias, offline recycle area, interrupted run, cross-run undo, `.git` and
    `node_modules` changes, cancelled and unchecked items, a different file at the source path, a change racing the
    snapshot with 5,000 and 20,000 files) against temp trees; none lost data.
  - `PlanExecutorTests` add: a file dated in the future does not block its copy forever.
  - Core.Tests has 359 tests, Gui.Tests 3; all 362 pass, three runs in a row.

## Bootstrap Validation

For the infrastructure refresh, both new PowerShell scripts parsed without
errors, the A2A JSON examples parsed successfully, and a second updater run
created no additional files or review copies. These checks do not establish
A2A runtime interoperability or exercise branch deletion and skill publication.

Last updated: 2026-10-23.
