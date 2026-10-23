<#
.SYNOPSIS
    Print the release notes of one version from docs/release-notes.md.

.DESCRIPTION
    A version's entry starts with a "## <version>" heading and runs until the next "## " heading. Its line starting
    with "Summary:" is the plain-text summary the in-app update check shows (at most 500 characters). Without
    -Summary the whole entry is printed, which the release workflow uses as the GitHub release text. Fails when the
    version has no entry or no summary line.

.PARAMETER Version
    Version to print, such as 0.1.0.

.PARAMETER Summary
    Print only the summary text.

.PARAMETER Path
    Release notes file. Defaults to docs/release-notes.md in this repository.

.EXAMPLE
    .\scripts\windows\Get-ReleaseNotes.ps1 -Version 0.1.0 -Summary
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [switch]$Summary,

    [Parameter(Mandatory = $false)]
    [string]$Path = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'docs/release-notes.md')
)

$ErrorActionPreference = 'Stop'
$Lines = [IO.File]::ReadAllLines($Path, [Text.Encoding]::UTF8)
$Start = -1
for ($Index = 0; $Index -lt $Lines.Length; $Index++) {
    if ($Lines[$Index].Trim() -eq "## $Version") { $Start = $Index + 1; break }
}
if ($Start -lt 0) { throw "docs/release-notes.md has no '## $Version' entry." }

$Entry = [Collections.Generic.List[string]]::new()
for ($Index = $Start; $Index -lt $Lines.Length -and -not $Lines[$Index].StartsWith('## '); $Index++) {
    $Entry.Add($Lines[$Index])
}
$SummaryLine = $Entry | Where-Object { $_.StartsWith('Summary:') } | Select-Object -First 1
if (-not $SummaryLine) { throw "The $Version entry in docs/release-notes.md has no 'Summary:' line." }
$SummaryText = $SummaryLine.Substring('Summary:'.Length).Trim()
if ($SummaryText.Length -eq 0 -or $SummaryText.Length -gt 500) { throw "The $Version summary must be 1 to 500 characters." }

if ($Summary) {
    Write-Output $SummaryText
} else {
    Write-Output (($Entry -join "`n").Trim())
}
