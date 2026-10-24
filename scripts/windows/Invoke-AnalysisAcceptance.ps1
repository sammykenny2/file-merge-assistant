<#
.SYNOPSIS
    Measure experimental exact analysis on newly generated, retained Windows fixtures.
.DESCRIPTION
    Builds a Debug acceptance host and the pinned Release native adapter, then runs
    engines sequentially at provisional 5k/50k/300k primary-file scales. Six sources
    include known duplicates, real synthetic PNGs, regenerable/excluded files,
    Unicode, long paths, a hardlink and a skipped junction. Measures fresh/warm/+1%
    phases and verifies simulated offline recovery. Never scans personal files,
    applies operation plans, installs, publishes or deletes fixture directories.
    Large runs can take substantial time and space. OS cache is not purged.
.PARAMETER FileCounts
    Provisional primary-file counts. Default 5000,50000,300000; 1000 is a smoke case.
.PARAMETER Engines
    csharp and/or czkawka, run serially. Both remain Experimental.
.PARAMETER CaseTimeoutMinutes
    Wall-clock limit for the entire owned child case, including generation and validation.
.EXAMPLE
    .\scripts\windows\Invoke-AnalysisAcceptance.ps1 -FileCounts 5000
.EXAMPLE
    .\scripts\windows\Invoke-AnalysisAcceptance.ps1
#>
#requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()][ValidateRange(1000, 300000)][int[]]$FileCounts = @(5000, 50000, 300000),
    [ValidateNotNullOrEmpty()][ValidateSet('csharp', 'czkawka')][string[]]$Engines = @('csharp', 'czkawka'),
    [ValidateRange(1, 240)][int]$CaseTimeoutMinutes = 120
)

$ErrorActionPreference = 'Stop'
if ($FileCounts.Count -eq 0 -or $Engines.Count -eq 0) { throw 'The acceptance matrix must not be empty.' }
$Engines = @($Engines | ForEach-Object { $_.ToLowerInvariant() })
if (@($FileCounts | Select-Object -Unique).Count -ne $FileCounts.Count -or @($Engines | Select-Object -Unique).Count -ne $Engines.Count) { throw 'Duplicate acceptance matrix entries are not allowed.' }
. (Join-Path $PSScriptRoot 'Invoke-OwnedProcess.ps1')
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not [Environment]::Is64BitProcess -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') { throw 'This acceptance runner currently validates Windows x64 only.' }
$build = Join-Path $root 'build'
if ((Test-Path -LiteralPath $build) -and ((Get-Item -LiteralPath $build -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'build/ must not be a reparse point.' }
$run = Join-Path $build ('acceptance-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$report = [ordered]@{
    formatVersion = 1; kind = 'windows-synthetic-acceptance-suite'; startedUtc = [DateTime]::UtcNow.ToString('o')
    sourceCommit = ((& git -C $root rev-parse HEAD) -join '').Trim()
    sourceWorktreeDirty = [bool]((& git -C $root -c core.excludesFile=.gitignore status --porcelain) -join '')
    passed = $false; provisionalPrimaryFileCounts = $FileCounts; engines = $Engines; cases = @()
    hardware = $null; hardwareError = $null
    caseTimeoutMinutes = $CaseTimeoutMinutes
}
function Write-SuiteReport {
    [IO.File]::WriteAllText((Join-Path $run 'suite.json'), ($report | ConvertTo-Json -Depth 10) + "`n", [Text.UTF8Encoding]::new($false))
}
try {
    try {
        $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
        $computer = Get-CimInstance Win32_ComputerSystem
        $os = Get-CimInstance Win32_OperatingSystem
        $report.hardware = [ordered]@{
            cpu = $cpu.Name; logicalProcessors = $computer.NumberOfLogicalProcessors
            physicalMemoryBytes = $computer.TotalPhysicalMemory; os = $os.Caption; build = $os.BuildNumber
            osVersion = $os.Version; architecture = $os.OSArchitecture
        }
    } catch { $report.hardwareError = $_.Exception.Message }
    $dotnet = (& dotnet --version) -join ''
    if ($LASTEXITCODE -ne 0) { throw 'The .NET SDK is required.' }
    $report.dotnetSdk = $dotnet.Trim()
    $native = Join-Path $root 'native/fma-czkawka-ffi/target/x86_64-pc-windows-msvc/release'
    if ('czkawka' -in $Engines) {
        & (Join-Path $PSScriptRoot 'Build-NativeAnalysis.ps1') -Configuration Release
        if ($LASTEXITCODE -ne 0) { throw 'Native adapter build failed.' }
        $report.nativeSha256 = (Get-FileHash -LiteralPath (Join-Path $native 'fma_czkawka_ffi.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    & dotnet build (Join-Path $root 'tests/acceptance-host/AcceptanceHost.csproj') --configuration Debug --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Acceptance host build failed.' }
    $hostDll = Join-Path $root 'tests/acceptance-host/bin/Debug/net10.0-windows/FileMergeAssistant.AcceptanceHost.dll'
    $report.hostSha256 = (Get-FileHash -LiteralPath $hostDll -Algorithm SHA256).Hash.ToLowerInvariant()
    $report.coreSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $hostDll) 'FileMergeAssistant.Core.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    foreach ($count in $FileCounts) {
        foreach ($engine in $Engines) {
            $case = Join-Path $run "$engine-$count"
            $log = Join-Path $run "$engine-$count.jsonl"
            $caseRecord = [ordered]@{ engine = $engine; primaryFiles = $count; exitCode = $null; status = 'Pending'; report = (Join-Path $case 'report.json') }
            $report.cases += $caseRecord
            try {
                Invoke-OwnedProcess -Executable (Get-Command dotnet).Source -Arguments @($hostDll, '--root', $case, '--count', [string]$count,
                    '--engine', $engine, '--fixtures', (Join-Path $root 'tests/Fixtures'), '--native', $native) `
                    -LogPath $log -TimeoutSeconds ($CaseTimeoutMinutes * 60) -Record $caseRecord -OnUpdate { Write-SuiteReport }
                if ($caseRecord.exitCode -ne 0) { throw "Acceptance failed for $engine/$count; inspect $case/report.json and $log." }
                $caseReport = Get-Content -LiteralPath $caseRecord.report -Raw -Encoding UTF8 | ConvertFrom-Json
                if (-not $caseReport.passed -or $caseReport.nominalPrimaryFiles -ne $count -or $caseReport.engine.id -ne $engine) {
                    throw 'The child report does not match the requested completed case.'
                }
                $caseRecord.status = 'Passed'
            } catch {
                if ($caseRecord.status -ne 'TimedOut') { $caseRecord.status = 'Failed' }
                $caseRecord['error'] = $_.Exception.Message
                throw
            } finally { Write-SuiteReport }
        }
    }
    if ($report.cases.Count -ne $FileCounts.Count * $Engines.Count -or @($report.cases | Where-Object { $_.status -ne 'Passed' }).Count -ne 0) { throw 'The acceptance matrix did not complete.' }
    $report.passed = $true
} finally {
    $report.finishedUtc = [DateTime]::UtcNow.ToString('o')
    Write-SuiteReport
    Write-Host "Acceptance suite: $(Join-Path $run 'suite.json')"
}
