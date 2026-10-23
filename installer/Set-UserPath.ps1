<#
.SYNOPSIS
    Add or remove one folder in the current user's PATH without changing any other entry.

.DESCRIPTION
    Used by the File Merge Assistant setup program and uninstaller (installer/FileMergeAssistant.nsi) for the
    optional fma command-line tool. NSIS string operations are limited to 1024 characters and would truncate a long
    PATH, so the registry is edited here instead.

    The value is read without expanding variables such as %USERPROFILE%, and its registry kind is kept
    (REG_EXPAND_SZ stays REG_EXPAND_SZ). Entries are compared case-insensitively, ignoring surrounding spaces and a
    trailing backslash. Adding appends ";<folder>" only when the folder is missing; removing drops exactly the
    matching entries and keeps the rest of the text as it was, so adding and then removing restores the original
    value. Nothing is written when nothing changes. The caller broadcasts WM_SETTINGCHANGE.

.PARAMETER Add
    Folder to append to PATH if it is not already listed.

.PARAMETER Remove
    Folder to remove from PATH.

.PARAMETER KeyPath
    Registry key under HKEY_CURRENT_USER that holds the value: Environment for the real PATH, a scratch key in tests.
    It has no default on purpose, so a mistyped command line fails instead of editing the real PATH.

.PARAMETER ValueName
    Name of the value to edit. Defaults to Path.

.EXAMPLE
    .\Set-UserPath.ps1 -KeyPath Environment -Add "$env:LOCALAPPDATA\Programs\FileMergeAssistant\cli"

.EXAMPLE
    .\Set-UserPath.ps1 -KeyPath Environment -Remove "$env:LOCALAPPDATA\Programs\FileMergeAssistant\cli"
#>
[CmdletBinding(DefaultParameterSetName = 'Add')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Add')]
    [string]$Add,

    [Parameter(Mandatory = $true, ParameterSetName = 'Remove')]
    [string]$Remove,

    [Parameter(Mandatory = $true)]
    [string]$KeyPath,

    [Parameter(Mandatory = $false)]
    [string]$ValueName = 'Path'
)

$ErrorActionPreference = 'Stop'

function ConvertTo-PathKey([string]$Entry) {
    return $Entry.Trim().TrimEnd('\').ToUpperInvariant()
}

$Folder = if ($PSCmdlet.ParameterSetName -eq 'Add') { $Add } else { $Remove }
if ([string]::IsNullOrWhiteSpace($Folder) -or $Folder.Contains(';')) {
    throw "Invalid folder: '$Folder'."
}
$Target = ConvertTo-PathKey $Folder

$Key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($KeyPath, $true)
try {
    $Exists = $Key.GetValueNames() -contains $ValueName
    $Current = if ($Exists) {
        [string]$Key.GetValue($ValueName, '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    } else { '' }
    $Kind = if ($Exists) { $Key.GetValueKind($ValueName) } else { [Microsoft.Win32.RegistryValueKind]::ExpandString }
    $Entries = $Current -split ';'
    $Listed = @($Entries | Where-Object { $_.Trim() -ne '' -and (ConvertTo-PathKey $_) -eq $Target }).Count -gt 0

    if ($PSCmdlet.ParameterSetName -eq 'Add') {
        if ($Listed) { Write-Output "Already in PATH: $Folder"; return }
        $Updated = if ($Current -eq '') { $Folder } else { "$Current;$Folder" }
        $Key.SetValue($ValueName, $Updated, $Kind)
        Write-Output "Added to PATH: $Folder"
        return
    }

    if (-not $Listed) { Write-Output "Not in PATH: $Folder"; return }
    $Kept = @($Entries | Where-Object { $_.Trim() -eq '' -or (ConvertTo-PathKey $_) -ne $Target })
    $Updated = $Kept -join ';'
    if ($Updated -eq '') {
        $Key.DeleteValue($ValueName)
    } else {
        $Key.SetValue($ValueName, $Updated, $Kind)
    }
    Write-Output "Removed from PATH: $Folder"
} finally {
    $Key.Dispose()
}
