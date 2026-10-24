<#
.SYNOPSIS
    Define a bounded child-process runner for developer acceptance scripts.
.DESCRIPTION
    Dot-source this file, then call Invoke-OwnedProcess. Uses explicit arguments,
    drains stdout/stderr asynchronously, updates a supplied case record before
    starting, and terminates only the process tree it created on timeout/error.
    Does not start any process when dot-sourced.
.EXAMPLE
    . .\scripts\windows\Invoke-OwnedProcess.ps1
#>
#requires -Version 7.2

function Invoke-OwnedProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$LogPath,
        [Parameter(Mandatory)][ValidateRange(1, 86400)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][Collections.IDictionary]$Record,
        [Parameter(Mandatory)][scriptblock]$OnUpdate
    )
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $writer = [IO.StreamWriter]::new($LogPath, $false, [Text.UTF8Encoding]::new($false))
    $writer.AutoFlush = $true
    $errorWriter = [IO.StreamWriter]::new("$LogPath.stderr.txt", $false, [Text.UTF8Encoding]::new($false))
    $errorWriter.AutoFlush = $true
    $started = $false
    $Record['status'] = 'Starting'
    $Record['startedUtc'] = [DateTime]::UtcNow.ToString('o')
    try {
        & $OnUpdate
        if (-not $process.Start()) { throw 'Child process did not start.' }
        $started = $true
        $Record['pid'] = $process.Id
        $Record['status'] = 'Running'
        & $OnUpdate
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $outputTask = $process.StandardOutput.ReadLineAsync()
        $errorTask = $process.StandardError.ReadLineAsync()
        $outputDone = $false; $errorDone = $false
        while (-not ($process.HasExited -and $outputDone -and $errorDone)) {
            if (-not $process.HasExited -and $clock.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
                $Record['status'] = 'TimedOut'
                $Record['error'] = "Owned child exceeded $TimeoutSeconds seconds."
                & $OnUpdate
                $process.Kill($true)
                if (-not $process.WaitForExit(10000)) { throw 'Owned child did not exit after termination.' }
            }
            $outputBudget = 64
            while ($outputBudget-- -gt 0 -and -not $outputDone -and $outputTask.IsCompleted) {
                $line = $outputTask.GetAwaiter().GetResult()
                if ($null -eq $line) { $outputDone = $true }
                else {
                    $writer.WriteLine($line)
                    Write-Host $line
                    $outputTask = $process.StandardOutput.ReadLineAsync()
                }
            }
            $errorBudget = 64
            while ($errorBudget-- -gt 0 -and -not $errorDone -and $errorTask.IsCompleted) {
                $line = $errorTask.GetAwaiter().GetResult()
                if ($null -eq $line) { $errorDone = $true }
                else {
                    $errorWriter.WriteLine($line)
                    Write-Host $line
                    $errorTask = $process.StandardError.ReadLineAsync()
                }
            }
            if ($process.HasExited -and $clock.Elapsed.TotalSeconds -gt $TimeoutSeconds + 15) { throw 'Child output did not close before the drain deadline.' }
            if (-not ($process.HasExited -and $outputDone -and $errorDone)) { Start-Sleep -Milliseconds 100 }
        }
        $Record['exitCode'] = $process.ExitCode
        if ($Record['status'] -eq 'TimedOut') { throw [TimeoutException]::new($Record['error']) }
        $Record['status'] = 'Exited'
    } catch {
        if ($Record['status'] -ne 'TimedOut') { $Record['status'] = 'Failed' }
        $Record['error'] = $_.Exception.Message
        throw
    } finally {
        if ($started -and -not $process.HasExited) {
            $process.Kill($true)
            $null = $process.WaitForExit(10000)
        }
        $Record['finishedUtc'] = [DateTime]::UtcNow.ToString('o')
        $writer.Dispose(); $errorWriter.Dispose(); $process.Dispose()
        & $OnUpdate
    }
}
