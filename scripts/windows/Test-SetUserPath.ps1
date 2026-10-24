<#
.SYNOPSIS
    Test installer/Set-UserPath.ps1 against a scratch registry key.

.DESCRIPTION
    Creates HKCU\Software\FileWorkbenchTests\<guid>, runs the PATH helper against values in that key and
    deletes the key afterwards. The real HKCU\Environment\Path is never read or written. Checks long values, the
    REG_EXPAND_SZ kind, unexpanded %VARIABLES%, trailing separators, case-insensitive matches, idempotent adds and
    that add-then-remove restores the original text exactly. Exits with code 1 when a check fails.

.EXAMPLE
    .\scripts\windows\Test-SetUserPath.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Helper = Join-Path $ProjectRoot 'installer/Set-UserPath.ps1'
$KeyPath = "Software\FileWorkbenchTests\$([guid]::NewGuid().ToString('N'))"
$Failures = 0

function Set-Value([string]$Value, [Microsoft.Win32.RegistryValueKind]$Kind) {
    $Key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($KeyPath, $true)
    try { $Key.SetValue('Path', $Value, $Kind) } finally { $Key.Dispose() }
}

function Get-Value {
    $Key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($KeyPath)
    try {
        if (-not ($Key.GetValueNames() -contains 'Path')) { return $null }
        return [pscustomobject]@{
            Text = [string]$Key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            Kind = $Key.GetValueKind('Path')
        }
    } finally { $Key.Dispose() }
}

function Invoke-Helper([string]$Mode, [string]$Folder) {
    $Arguments = @{ KeyPath = $KeyPath }
    $Arguments[$Mode] = $Folder
    & $Helper @Arguments | Out-Null
}

function Assert-Equal($Expected, $Actual, [string]$Name) {
    if ($Expected -ceq $Actual) { Write-Output "PASS $Name" }
    else { Write-Output "FAIL $Name`n  expected: $Expected`n  actual:   $Actual"; $script:Failures++ }
}

$Cli = 'C:\Users\someone\AppData\Local\Programs\FileWorkbench\cli'
try {
    $Long = (1..120 | ForEach-Object { "C:\Tools\Folder$_\bin" }) -join ';'
    $Original = "%USERPROFILE%\bin;$Long;%LOCALAPPDATA%\Microsoft\WindowsApps"
    Set-Value $Original ([Microsoft.Win32.RegistryValueKind]::ExpandString)
    Invoke-Helper 'Add' $Cli
    $AfterAdd = Get-Value
    Assert-Equal "$Original;$Cli" $AfterAdd.Text 'add appends to a long REG_EXPAND_SZ value without expanding it'
    Assert-Equal 'ExpandString' "$($AfterAdd.Kind)" 'add keeps REG_EXPAND_SZ'
    Invoke-Helper 'Add' "$($Cli.ToUpperInvariant())\"
    Assert-Equal "$Original;$Cli" (Get-Value).Text 'add is idempotent, ignoring case and a trailing backslash'
    Invoke-Helper 'Remove' $Cli
    $AfterRemove = Get-Value
    Assert-Equal $Original $AfterRemove.Text 'remove restores the original text'
    Assert-Equal 'ExpandString' "$($AfterRemove.Kind)" 'remove keeps REG_EXPAND_SZ'

    Set-Value 'C:\A;C:\B;' ([Microsoft.Win32.RegistryValueKind]::String)
    Invoke-Helper 'Add' $Cli
    Invoke-Helper 'Remove' $Cli
    $Plain = Get-Value
    Assert-Equal 'C:\A;C:\B;' $Plain.Text 'add-then-remove keeps a trailing separator'
    Assert-Equal 'String' "$($Plain.Kind)" 'REG_SZ stays REG_SZ'

    Invoke-Helper 'Remove' 'C:\Not\There'
    Assert-Equal 'C:\A;C:\B;' (Get-Value).Text 'removing a missing folder changes nothing'

    $Key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($KeyPath, $true)
    try { $Key.DeleteValue('Path') } finally { $Key.Dispose() }
    Invoke-Helper 'Add' $Cli
    $Created = Get-Value
    Assert-Equal $Cli $Created.Text 'add creates a missing value'
    Assert-Equal 'ExpandString' "$($Created.Kind)" 'a created value is REG_EXPAND_SZ'
    Invoke-Helper 'Remove' $Cli
    Assert-Equal $null (Get-Value) 'removing the only entry deletes the value'
} finally {
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($KeyPath, $false)
    $Parent = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\FileWorkbenchTests')
    if ($Parent -and $Parent.SubKeyCount -eq 0 -and $Parent.ValueCount -eq 0) {
        $Parent.Dispose()
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey('Software\FileWorkbenchTests', $false)
    } elseif ($Parent) { $Parent.Dispose() }
}

if ($Failures -gt 0) { Write-Output "$Failures check(s) failed."; exit 1 }
Write-Output 'All Set-UserPath checks passed.'
