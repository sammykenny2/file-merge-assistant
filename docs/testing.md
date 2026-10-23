# Testing Strategy

File Merge Assistant uses xUnit for the .NET 8 Windows-targeted core library.
Tests live in `tests/Core.Tests/`, grouped under `Hashing/`, `Reporting/`,
`Resolution/`, and `Services/`. NSubstitute supplies test doubles; file-operation
fixtures create temporary directories and clean them up after each test.

## Running Tests

Run from the repository root on Windows with the .NET 8 SDK installed:

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

Checked on 2026-10-23 with SDK 8.0.426, with `TreatWarningsAsErrors` and NuGet
audit at their defaults:

- `dotnet build file-merge-assistant.sln` succeeds for the Debug configuration.
- `dotnet test tests/Core.Tests/Core.Tests.csproj` passes all 147 tests; VSTest
  reports about 0.2 seconds of test time.

To guard against future hangs, add a bounded timeout when diagnosing:

```powershell
dotnet test tests/Core.Tests/Core.Tests.csproj --blame-hang-timeout 60s --blame-hang-dump-type none
```

VSTest writes its sequence attachment under `tests/Core.Tests/TestResults/`.

### Fixes That Established This Baseline

- CA1716: `IWatchService.Start()`/`Stop()` are now `StartWatching()`/`StopWatching()`.
- CA1859: `PreviewStatisticsService.BuildDirectoryBreakdown()` returns `List<T>`.
- CA1305: `ScannedFileViewModel.DateTaken` formats with the invariant culture.
  This blocked the solution build, not the test build.
- `EmptyDirectoryService.Cleanup(dryRun: true)` never returned when the tree held
  an empty directory, which hung `Cleanup_DryRun_Does_Not_Delete`. Cleanup now
  makes one deepest-first pass, and a dry run reports the same count as a real
  cleanup. Two regression tests cover nested and mixed trees.
- NuGet audit errors NU1902/NU1903: `SixLabors.ImageSharp` moved from 2.1.13 to
  4.1.3, which fixes GHSA-gwg2-r3hj-4w44, GHSA-wmxv-xphr-5c9g,
  GHSA-j3p4-wp97-rph4, GHSA-j9gm-c75j-xc9q and GHSA-jjfr-hcj7-qf5w.

### Known Remaining Issues

- **Release builds fail without a Six Labors license key.** ImageSharp 4.x is
  under the Six Labors Split License; this MIT-licensed project qualifies for its
  Apache 2.0 terms, but the package validates a key at build time. Debug builds
  only warn. Provide the key through `SixLaborsLicenseKey` or
  `SixLaborsLicenseFile` outside the repository; `sixlabors.lic` is ignored by Git.
  Obtaining the key is a prerequisite for release packaging (PLAN-001 Phase 7).
- **Transitive packages with known vulnerabilities** (reported by
  `dotnet list package --vulnerable --include-transitive`; they do not fail the
  current build):
  - Gui: System.Text.Json 8.0.0 (GHSA-hh2w-p6rv-4g7w, GHSA-8g4q-xg66-9fp4).
  - Core.Tests: System.Net.Http 4.3.0 (GHSA-7jgj-8wvc-jh57) and
    System.Text.RegularExpressions 4.3.0 (GHSA-cmhx-cq75-c4mj).

  These are to be resolved or explicitly accepted with the .NET 10 upgrade
  (PLAN-001 Task 0.4).

## Bootstrap Validation

For the infrastructure refresh, both new PowerShell scripts parsed without
errors, the A2A JSON examples parsed successfully, and a second updater run
created no additional files or review copies. These checks do not establish
A2A runtime interoperability or exercise branch deletion and skill publication.

Last updated: 2026-10-23.
