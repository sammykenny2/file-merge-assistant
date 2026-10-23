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

## Known Baseline Issues

The bootstrap refresh was checked on 2026-10-23 with SDK 8.0.426. No application
or test source was changed by that refresh.

- The normal test build stops on existing CA1716 in `IWatchService.Stop()` and
  CA1859 in `PreviewStatisticsService.BuildDirectoryBreakdown()`, because
  `Directory.Build.props` enables `TreatWarningsAsErrors`.
- For diagnosis, building with `-p:TreatWarningsAsErrors=false` leaves those
  findings as warnings without changing the project configuration.
- The diagnostic test run passed 141 tests, then aborted after 30 seconds without
  progress while `EmptyDirectoryServiceTests.Cleanup_DryRun_Does_Not_Delete`
  was running. The suite is not recorded as passing.

The bounded diagnostic command, after that diagnostic build, was:

```powershell
dotnet test tests/Core.Tests/Core.Tests.csproj --no-build --no-restore --blame-hang-timeout 30s --blame-hang-dump-type none
```

VSTest writes its sequence attachment under `tests/Core.Tests/TestResults/`.
These existing issues require separate application/test work.

## Bootstrap Validation

For the infrastructure refresh, both new PowerShell scripts parsed without
errors, the A2A JSON examples parsed successfully, and a second updater run
created no additional files or review copies. These checks do not establish
A2A runtime interoperability or exercise branch deletion and skill publication.

Last updated: 2026-10-23.
