# Testing Strategy

File Merge Assistant uses xUnit for the .NET 10 Windows-targeted core library.
Tests live in `tests/Core.Tests/`, grouped under `Hashing/`, `Reporting/`,
`Resolution/`, `Services/`, `Stacks/`, `Updates/` and `Workbench/`. NSubstitute supplies test
doubles; file-operation fixtures create temporary directories and clean them up after each test.

`tests/Gui.Tests/` checks the WPF workbench. `ScreenRenderingTests` walks through every screen
with the demo data on one STA thread (`WpfHost`), renders the whole window body at 1440 px wide
with `RenderTargetBitmap`, and fails on any WPF binding error. `RealIndexRenderingTests` does the
same over the real index after scanning a throwaway tree under `%TEMP%`.

The scanner and real-store tests (`Scanning/`, `Workbench/IndexedWorkbenchStoreTests`) build folder
trees under `%TEMP%` (`TempTree`) and an index under `FMA_DATA_DIR`. No test scans or reads the real
user profile; the first-run folder suggestions are not exercised against the real store. It needs no visible desktop, so it
also runs while the session is locked. Set `FMA_SNAPSHOT_DIR` to a folder to save each screen as
a PNG:

```powershell
$env:FMA_SNAPSHOT_DIR = "$env:TEMPma-shots"; dotnet test tests/Gui.Tests/Gui.Tests.csproj
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

## Bootstrap Validation

For the infrastructure refresh, both new PowerShell scripts parsed without
errors, the A2A JSON examples parsed successfully, and a second updater run
created no additional files or review copies. These checks do not establish
A2A runtime interoperability or exercise branch deletion and skill publication.

Last updated: 2026-10-23.
