<#
.SYNOPSIS
    Validate File Workbench install, reinstall/upgrade and uninstall in a disposable Windows Sandbox or VM.
.DESCRIPTION
    Refuses a pre-existing installation, File Workbench process, startup entry, shortcut or
    default data directory. Installs only under a new report directory, exercises
    the actual packaged CLI, verifies CLI choice/PATH preservation on upgrade,
    then checks uninstall preserves unrelated CLI files and user data. Does not
    remove failed-test artifacts or restore registry values to hide a failure.
    Never run this on a personal workstation. Use the non-installing package test there.
.PARAMETER Installer
    Current installer. Its adjacent .sha256 file must match.
.PARAMETER FixturesRoot
    Directory containing analysis and similar-images fixtures.
.PARAMETER ReportParent
    Local directory for new, retained validation reports and the test installation.
.PARAMETER BaselineInstaller
    Optional earlier installer with adjacent .sha256. Without it, the upgrade step
    is explicitly reported as a same-version reinstall, not a version upgrade.
.PARAMETER DisposableMachine
    Required acknowledgement that this is a disposable Sandbox/VM user profile.
.EXAMPLE
    .\Test-InstallerLifecycle.ps1 -Installer C:\FileWorkbenchKit\FileWorkbench-0.2.1-x64-debug-setup.exe -FixturesRoot C:\FileWorkbenchKit\Fixtures -ReportParent C:\FmaReports -DisposableMachine
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$FixturesRoot,
    [Parameter(Mandatory)][string]$ReportParent,
    [string]$BaselineInstaller,
    [switch]$DisposableMachine
)

$ErrorActionPreference = 'Stop'
if (-not $DisposableMachine) { throw 'This installs/uninstalls software. Run only in a disposable Windows Sandbox/VM with -DisposableMachine.' }
if (-not [Environment]::Is64BitProcess -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') { throw 'Windows x64 PowerShell is required.' }

function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Check-Checksum([string]$Path) {
    Require (Test-Path -LiteralPath $Path -PathType Leaf) "Missing installer: $Path"
    $expected = ((Get-Content -LiteralPath "$Path.sha256" -Raw) -split '\s+')[0]
    Require ($expected -match '^[0-9a-fA-F]{64}$' -and (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $expected) "Installer checksum mismatch: $Path"
}
function Assert-NoReparse([string]$Path) {
    $probe = [IO.Path]::GetFullPath($Path)
    while ($probe) {
        if (Test-Path -LiteralPath $probe) {
            Require (-not ((Get-Item -LiteralPath $probe -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) "Validation path traverses a reparse point: $probe"
        }
        $parent = Split-Path -Parent $probe
        if ($parent -eq $probe) { break }
        $probe = $parent
    }
}
function Read-UserPath {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment')
    try {
        if ($key -and $key.GetValueNames() -contains 'Path') {
            return [pscustomobject]@{ exists = $true; text = $key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames); kind = [string]$key.GetValueKind('Path') }
        }
        return [pscustomobject]@{ exists = $false; text = ''; kind = $null }
    } finally { if ($key) { $key.Dispose() } }
}
function Assert-OriginalPath {
    $current = Read-UserPath
    Require ($current.exists -eq $originalPath.exists -and $current.text -ceq $originalPath.text -and $current.kind -eq $originalPath.kind) 'Unrelated user PATH content or registry kind changed.'
}
function Invoke-Setup([string]$Executable, [string]$Arguments, [string]$Name, [int]$ExpectedExit = 0) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $Executable; $start.Arguments = $Arguments
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    try {
        $null = $process.Start()
        if (-not $process.WaitForExit(180000)) { $process.Kill(); throw "Installer timeout: $Name" }
        $steps.Add([ordered]@{ name = $Name; exitCode = $process.ExitCode })
        Require ($process.ExitCode -eq $ExpectedExit) "Installer $Name exited $($process.ExitCode), expected $ExpectedExit."
    } finally { $process.Dispose() }
}

$Installer = [IO.Path]::GetFullPath($Installer)
$FixturesRoot = [IO.Path]::GetFullPath($FixturesRoot)
Check-Checksum $Installer
if ($BaselineInstaller) { $BaselineInstaller = [IO.Path]::GetFullPath($BaselineInstaller); Check-Checksum $BaselineInstaller }
$currentVersion = [version]([Diagnostics.FileVersionInfo]::GetVersionInfo($Installer).ProductVersion)
$firstInstaller = if ($BaselineInstaller) { $BaselineInstaller } else { $Installer }
$baselineVersion = [version]([Diagnostics.FileVersionInfo]::GetVersionInfo($firstInstaller).ProductVersion)
Require ($baselineVersion -le $currentVersion) 'The supplied baseline is newer than the current installer; downgrade validation is out of scope.'
$currentHash = (Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash.ToLowerInvariant()
$baselineHash = (Get-FileHash -LiteralPath $firstInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
$upgradeKind = if ($baselineVersion -lt $currentVersion) { 'lower-version-upgrade' }
    elseif ($baselineHash -ne $currentHash) { 'same-version-payload-replacement' } else { 'same-version-reinstall' }
$appKey = 'HKCU:\Software\FileMergeAssistant'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FileMergeAssistant'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$data = Join-Path $env:LOCALAPPDATA 'FileWorkbench'
$legacyData = Join-Path $env:LOCALAPPDATA 'FileMergeAssistant'
$shortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'File Workbench.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'File Workbench.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'File Merge Assistant.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'File Merge Assistant.lnk')
)
Require (-not (Test-Path -LiteralPath $appKey) -and -not (Test-Path -LiteralPath $uninstallKey)) 'An File Workbench installation is already registered; use a fresh disposable user profile.'
Require (@(Get-Process -Name FileWorkbench,file-workbench,FileMergeAssistant,fma -ErrorAction SilentlyContinue).Count -eq 0) 'A File Workbench or legacy FMA process is running; use a fresh VM.'
Require (-not (Get-ItemProperty -LiteralPath $runKey -Name FileMergeAssistant -ErrorAction SilentlyContinue)) 'A startup entry already exists.'
Require (-not (Test-Path -LiteralPath $data) -and -not (Test-Path -LiteralPath $legacyData)) 'A current or legacy data folder already exists; use a fresh disposable profile.'
foreach ($shortcut in $shortcuts) { Require (-not (Test-Path -LiteralPath $shortcut)) 'An File Workbench shortcut already exists.' }
Assert-NoReparse $ReportParent
Assert-NoReparse $env:LOCALAPPDATA
$originalPath = Read-UserPath
$run = Join-Path ([IO.Path]::GetFullPath($ReportParent)) ('lifecycle-' + [guid]::NewGuid().ToString('N'))
$install = Join-Path $run 'test installation'
Require ($install.StartsWith($run + '\', [StringComparison]::OrdinalIgnoreCase)) 'Invalid owned installation target.'
New-Item -ItemType Directory -Path $run | Out-Null
$steps = [Collections.Generic.List[object]]::new()
$checks = [Collections.Generic.List[string]]::new()
$report = [ordered]@{
    formatVersion = 1; kind = 'disposable-machine-installer-lifecycle'; startedUtc = [DateTime]::UtcNow.ToString('o')
    platform = [Environment]::OSVersion.VersionString; installerSha256 = $currentHash; installerVersion = [string]$currentVersion
    baselineSha256 = $baselineHash; baselineVersion = [string]$baselineVersion; upgradeKind = $upgradeKind
    passed = $false; guiInteraction = 'not-tested-by-this-script'; steps = @(); checks = @(); error = $null
}
try {
    $foreign = Join-Path $run 'foreign installation'
    New-Item -ItemType Directory -Path (Join-Path $foreign 'app') | Out-Null
    $foreignSentinel = Join-Path $foreign 'app/keep.txt'
    [IO.File]::WriteAllText($foreignSentinel, 'Foreign application content must survive.')
    Invoke-Setup $Installer "/S /D=$foreign" 'refuse-foreign-folder' 2
    Require ((Get-Content -LiteralPath $foreignSentinel -Raw) -eq 'Foreign application content must survive.') 'Foreign application content changed.'
    Require (-not (Test-Path -LiteralPath $appKey) -and -not (Test-Path -LiteralPath $uninstallKey)) 'Refused setup changed registration.'
    Assert-OriginalPath
    $checks.Add('foreign unmarked application folder refused before registration or PATH changes')

    Invoke-Setup $firstInstaller "/S /CLI /D=$install" 'install-with-cli'
    if (Test-Path -LiteralPath (Join-Path $install 'app/FileMergeAssistant.exe')) { $data = $legacyData }
    Require ((Get-ItemProperty -LiteralPath $appKey).InstallDir -eq $install) 'InstallDir does not match the owned target.'
    Require ((Get-ItemProperty -LiteralPath $appKey).CliInstalled -eq 1) 'CLI selection was not saved.'
    Require ((Test-Path -LiteralPath (Join-Path $install 'cli/file-workbench.exe')) -or
        (Test-Path -LiteralPath (Join-Path $install 'cli/fma.exe'))) 'CLI was not installed.'
    Require (@((Read-UserPath).text -split ';' | Where-Object { $_.Trim().TrimEnd('\') -eq (Join-Path $install 'cli') }).Count -eq 1) 'CLI PATH entry missing or duplicated.'
    New-Item -ItemType Directory -Path $data | Out-Null
    $dataSentinel = Join-Path $data 'validation-user-data.txt'
    [IO.File]::WriteAllText($dataSentinel, 'User data must survive upgrade and uninstall.')
    $otherCli = Join-Path $install 'cli/foreign-tool.txt'
    $otherRoot = Join-Path $install 'foreign-root.txt'
    foreach ($path in @($otherCli, $otherRoot)) { [IO.File]::WriteAllText($path, 'Unrelated file must survive.') }
    $obsolete = Join-Path $install 'app/obsolete-test-payload.txt'
    [IO.File]::WriteAllText($obsolete, 'Replace this owned application payload on upgrade.')
    $legacyStartup = Test-Path -LiteralPath (Join-Path $install 'app/FileMergeAssistant.exe')
    if ($legacyStartup) {
        New-Item -Path $runKey -Force | Out-Null
        New-ItemProperty -LiteralPath $runKey -Name FileMergeAssistant -Value ('"' + (Join-Path $install 'app/FileMergeAssistant.exe') + '" --launch-at-login') -PropertyType String | Out-Null
    }

    # No /CLI on the second call: setup must preserve the registered choice.
    Invoke-Setup $Installer "/S /D=$install" 'upgrade-preserves-cli-choice'
    Require (-not (Test-Path -LiteralPath (Join-Path $install 'cli/fma.exe'))) 'Upgrade left the legacy CLI executable.'
    if ($legacyStartup) {
        Require ((Get-ItemPropertyValue -LiteralPath $runKey -Name FileMergeAssistant) -eq
            ('"' + (Join-Path $install 'app/FileWorkbench.exe') + '" --launch-at-login')) 'Upgrade did not retarget its legacy startup command.'
        $checks.Add('legacy startup command retargeted without changing its Windows identity')
    }
    Require (-not (Test-Path -LiteralPath $obsolete)) 'Upgrade left obsolete owned app payload.'
    Require ((Get-ItemProperty -LiteralPath $appKey).CliInstalled -eq 1) 'Upgrade lost the CLI choice.'
    Require (@((Read-UserPath).text -split ';' | Where-Object { $_.Trim().TrimEnd('\') -eq (Join-Path $install 'cli') }).Count -eq 1) 'Upgrade duplicated or removed CLI PATH.'
    & (Join-Path $PSScriptRoot 'Test-AnalysisPackage.ps1') -PackageRoot $install -FixturesRoot $FixturesRoot -ReportParent $run
    $checks.Add('installed published CLI loads native exact/image capabilities without SDK or Rust paths')
    $checks.Add('reinstall/upgrade replaces obsolete app payload, preserving optional CLI selection and unrelated files')

    # Exercise the app-owned startup value; preflight proved it did not pre-exist.
    New-Item -Path $runKey -Force | Out-Null
    New-ItemProperty -LiteralPath $runKey -Name FileMergeAssistant -Value ('"' + (Join-Path $install 'app/FileWorkbench.exe') + '"') -PropertyType String -Force | Out-Null
    Invoke-Setup (Join-Path $install 'uninstall.exe') '/S' 'uninstall'
    # NSIS may run its uninstaller from a temporary child process. Observe completion explicitly.
    $wait = [Diagnostics.Stopwatch]::StartNew()
    while ((Test-Path -LiteralPath $appKey) -and $wait.Elapsed.TotalSeconds -lt 120) { Start-Sleep -Milliseconds 250 }
    Require (-not (Test-Path -LiteralPath $appKey) -and -not (Test-Path -LiteralPath $uninstallKey)) 'Uninstall registration remains.'
    Require (-not (Get-ItemProperty -LiteralPath $runKey -Name FileMergeAssistant -ErrorAction SilentlyContinue)) 'Uninstall kept its startup value.'
    foreach ($shortcut in $shortcuts) { Require (-not (Test-Path -LiteralPath $shortcut)) 'Uninstall kept its shortcut.' }
    Require (-not (Test-Path -LiteralPath (Join-Path $install 'app')) -and -not (Test-Path -LiteralPath (Join-Path $install 'cli/file-workbench.exe'))) 'Owned app or CLI payload remains.'
    foreach ($path in @($otherCli, $otherRoot)) { Require ((Get-Content -LiteralPath $path -Raw) -eq 'Unrelated file must survive.') 'Uninstall changed an unrelated file.' }
    Require ((Get-Content -LiteralPath $dataSentinel -Raw) -eq 'User data must survive upgrade and uninstall.') 'User data was changed or removed.'
    Assert-OriginalPath
    $checks.Add('uninstall removes owned payload/registration/startup/shortcuts and restores exact user PATH, preserving user data and foreign CLI/root files')
    $report.passed = $true
} catch {
    $report.error = $_.Exception.Message
    throw
} finally {
    $report.steps = @($steps); $report.checks = @($checks)
    $report.finishedUtc = [DateTime]::UtcNow.ToString('o')
    [IO.File]::WriteAllText((Join-Path $run 'report.json'), ($report | ConvertTo-Json -Depth 12) + "`n", [Text.UTF8Encoding]::new($false))
    Write-Host "Lifecycle report: $(Join-Path $run 'report.json')"
}
