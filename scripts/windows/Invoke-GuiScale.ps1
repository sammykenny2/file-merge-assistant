<#
.SYNOPSIS
    Measure synthetic 10k-model/10k-row WPF layout without opening a window.
.DESCRIPTION
    Builds a Debug-only test host and uses an isolated in-memory read-only store.
    Records finite-viewport render, filter, sort, scroll and dispatcher-queue
    delays. No physical input, real scan, native engine, installation or source
    operation is exercised. Retains reports; no cleanup. Uses a bounded owned
    child process and never stops the installed FMA.
.PARAMETER TimeoutSeconds
    Maximum wall-clock runtime of the newly created measurement child.
.EXAMPLE
    .\scripts\windows\Invoke-GuiScale.ps1
#>
#requires -Version 7.2
[CmdletBinding()]
param([ValidateRange(30, 600)][int]$TimeoutSeconds = 180)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-OwnedProcess.ps1')
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$build = Join-Path $root 'build'
if ((Test-Path -LiteralPath $build) -and ((Get-Item -LiteralPath $build -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'build/ must not be a reparse point.' }
$run = Join-Path $build ('gui-scale-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$record = [ordered]@{ kind = 'owned-offscreen-gui-probe'; status = 'Pending'; exitCode = $null; passed = $false }
$record.sourceCommit = ((& git -C $root rev-parse HEAD) -join '').Trim()
$record.sourceWorktreeDirty = [bool]((& git -C $root -c core.excludesFile=.gitignore status --porcelain) -join '')
function Write-ScaleReport {
    [IO.File]::WriteAllText((Join-Path $run 'process.json'), ($record | ConvertTo-Json -Depth 5) + "`n", [Text.UTF8Encoding]::new($false))
}
try {
    & dotnet build (Join-Path $root 'tests/gui-scale-host/GuiScaleHost.csproj') --configuration Debug --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'GUI scale host build failed.' }
    $hostDll = Join-Path $root 'tests/gui-scale-host/bin/Debug/net10.0-windows/FileMergeAssistant.GuiScaleHost.dll'
    Invoke-OwnedProcess -Executable (Get-Command dotnet).Source -Arguments @($hostDll, (Join-Path $run 'measurement')) `
        -LogPath (Join-Path $run 'events.jsonl') -TimeoutSeconds $TimeoutSeconds -Record $record -OnUpdate { Write-ScaleReport }
    if ($record.exitCode -ne 0) { throw 'GUI scale host failed; retained report/log has details.' }
    $report = Get-Content -LiteralPath (Join-Path $run 'measurement/report.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $report.passed -or $report.kind -ne 'offscreen-wpf-scale-diagnostic' -or @($report.measurements).Count -ne 23) { throw 'Incomplete or wrong GUI scale report.' }
    $record.status = 'Passed'; $record.passed = $true
} catch {
    if ($record.status -ne 'TimedOut') { $record.status = 'Failed' }
    $record['error'] = $_.Exception.Message
    throw
} finally { Write-ScaleReport; Write-Host "Retained GUI probe: $run" }
