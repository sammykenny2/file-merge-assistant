# Testing Strategy

File Merge Assistant uses xUnit for the .NET 10 Windows-targeted core library.
Tests live in `tests/Core.Tests/`, grouped under `Hashing/`, `Reporting/`,
`Resolution/`, and `Services/`. NSubstitute supplies test doubles; file-operation
fixtures create temporary directories and clean them up after each test.

## Running Tests

Run from the repository root on Windows with the .NET 10 SDK installed
(`global.json` selects the latest installed 10.0 feature band):

```powershell
dotnet test tests/Core.Tests/Core.Tests.csproj --verbosity minimal
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
  reports about 1 second of test time.
- `dotnet list file-merge-assistant.sln package --vulnerable --include-transitive`
  reports no vulnerable packages.
- Smoke check: `fma --help` and `fma scan` on a small folder succeed, and the
  GUI opens its main window and stays responsive.

The same build and tests also passed on SDK 8.0.426 with `net8.0-windows`
immediately before the upgrade.

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
  and System.IO.Hashing moved to 10.0.13, Serilog.Extensions.Logging to 10.0.0,
  and Serilog to 4.2.0, the minimum that Serilog.Extensions.Logging 10.0.0 needs.
  The upgrade also removed the transitive vulnerable packages that the .NET 8 build
  pulled in (System.Text.Json 8.0.0 in Gui; System.Net.Http and
  System.Text.RegularExpressions 4.3.0 in Core.Tests): the .NET 10 SDK prunes
  packages that the shared framework already provides.

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

## Bootstrap Validation

For the infrastructure refresh, both new PowerShell scripts parsed without
errors, the A2A JSON examples parsed successfully, and a second updater run
created no additional files or review copies. These checks do not establish
A2A runtime interoperability or exercise branch deletion and skill publication.

Last updated: 2026-10-23.
