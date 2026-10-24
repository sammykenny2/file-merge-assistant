<#
.SYNOPSIS
    Assemble existing local package artifacts and Windows VM validation scripts.
.DESCRIPTION
    Copies the installer, portable ZIP, native source ZIP, matching checksums,
    synthetic fixtures and validation scripts into a new build/ directory. No
    installation, publication or VM validation is performed. Use after Build-Installer.
.PARAMETER Configuration
    Debug (default) or Release artifacts to collect.
.EXAMPLE
    .\scripts\windows\New-AnalysisValidationKit.ps1
#>
[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw -Encoding UTF8
$version = @($props.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid application version.' }
$suffix = if ($Configuration -eq 'Debug') { '-debug' } else { '' }
$build = Join-Path $root 'build'
$kit = Join-Path $build ('windows-validation-' + [guid]::NewGuid().ToString('N'))
if ((Get-Item -LiteralPath $build -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'build/ must not be a reparse point.' }
$portable = "FileWorkbench-$version-x64$suffix-portable.zip"
$installer = "FileWorkbench-$version-x64$suffix-setup.exe"
$crate = Get-Content -LiteralPath (Join-Path $root 'native/file-workbench-czkawka-ffi/Cargo.toml') -Raw -Encoding UTF8
$engineVersion = [regex]::Match($crate, '(?m)^version = "(\d+\.\d+\.\d+)"').Groups[1].Value
if (-not $engineVersion) { throw 'Invalid native adapter version.' }
$source = "FileWorkbench-$version-czkawka-$engineVersion$suffix-source.zip"
$artifacts = @($installer, $portable, $source)
foreach ($name in $artifacts) {
    $path = Join-Path $build $name
    $expected = ((Get-Content -LiteralPath "$path.sha256" -Raw) -split '\s+')[0]
    if ($expected -notmatch '^[0-9a-fA-F]{64}$' -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) { throw "Artifact checksum mismatch: $name" }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $build $portable))
try {
    $entry = $zip.GetEntry('app/Licenses/Rust/SOURCE.json')
    if (-not $entry) { throw 'Portable package has no native source reference.' }
    $reader = [IO.StreamReader]::new($entry.Open(), [Text.Encoding]::UTF8)
    try { $reference = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ($reference.archive -ne $source -or $reference.sha256 -ne (Get-FileHash -LiteralPath (Join-Path $build $source) -Algorithm SHA256).Hash.ToLowerInvariant()) {
        throw 'Native source archive differs from the exact reference in the portable package; rebuild the artifacts together.'
    }
} finally { $zip.Dispose() }
New-Item -ItemType Directory -Path $kit | Out-Null
foreach ($name in $artifacts) {
    Copy-Item -LiteralPath (Join-Path $build $name), (Join-Path $build "$name.sha256") -Destination $kit
}
foreach ($name in @('Test-AnalysisPackage.ps1', 'Test-InstallerLifecycle.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $kit }
New-Item -ItemType Directory -Path (Join-Path $kit 'Fixtures') | Out-Null
foreach ($name in @('analysis', 'similar-images')) { Copy-Item -LiteralPath (Join-Path $root "tests/Fixtures/$name") -Destination (Join-Path $kit 'Fixtures') -Recurse }
$readme = @'
# Windows x64 validation kit

This is an experimental {{CONFIGURATION}} validation payload.
The GUI is WPF/Windows only. The native adapter is Release/static CRT; the .NET
application uses {{CONFIGURATION}}. No result in this kit certifies a clean machine.

This preview omits ImageSharp/CoenM and needs no Six Labors key. Legacy C# media
algorithms are explicitly unavailable; native PNG/JPEG review remains available.
Native notices preserve original files and two DeclaredSpdxTerms supplements
(realfft/rusty-chromaprint), with their manifests, README and source. Standard
terms are labeled separately and do not fabricate upstream copyright attribution.
Read the actual native manifest and source archive for this payload's provenance.

Copy this directory to C:\FmaKit on a disposable Windows x64 Sandbox or VM.
No SDK, .NET runtime or Rust installation is needed to run these packages.
Use Windows PowerShell (x64); the scripts also work with PowerShell 7.

Verify the delivered checksums before starting. The following prints mismatches
for files listed in CHECKSUMS.sha256 (it does not execute them):

    Set-Location C:\FmaKit
    Get-Content .\CHECKSUMS.sha256 | ForEach-Object {
        $hash, $name = $_ -split '  ', 2
        if ((Get-FileHash -LiteralPath $name -Algorithm SHA256).Hash -ne $hash) {
            throw "Checksum mismatch: $name"
        }
    }

First test the portable package without installing anything. Choose a new
extraction directory; do not overwrite an existing application:

    $portableRoot = Join-Path $env:TEMP ('file-workbench-portable-' + [guid]::NewGuid().ToString('N'))
    Expand-Archive -LiteralPath .\{{PORTABLE}} -DestinationPath $portableRoot
    .\Test-AnalysisPackage.ps1 -PackageRoot $portableRoot -FixturesRoot C:\FmaKit\Fixtures -ReportParent C:\FmaReports

For install/reinstall/uninstall validation, run ONLY in the disposable profile:

    .\Test-InstallerLifecycle.ps1 -Installer C:\FmaKit\{{INSTALLER}} -FixturesRoot C:\FmaKit\Fixtures -ReportParent C:\FmaReports -DisposableMachine

The script refuses an existing File Workbench installation, process, startup entry, shortcut
or data directory. It intentionally leaves reports and sentinel files for review.
It exercises silent NSIS install, preserved CLI selection, PATH, native loading,
replacement of obsolete app payload and uninstall ownership/user-data behavior.
Without -BaselineInstaller this is a same-version reinstall. To test a genuine
upgrade, supply an earlier installer and its adjacent SHA-256 file with that
parameter. The report distinguishes the two cases.

Also open the extracted GUI using a NEW test data directory:

    $env:FWB_DATA_DIR = Join-Path $env:TEMP ('file-workbench-gui-' + [guid]::NewGuid().ToString('N'))
    & "$portableRoot\app\FileWorkbench.exe"

Check English/Traditional
Chinese, engine selection, exact scan, image candidate review, cancel/reopen,
and high-DPI window behavior. Close the app before installer tests. Check
Tab/Shift+Tab entry/exit and focus cues for conflict/outcome lists; use
arrows/PageDown/Home/End to reach later rows and Space to choose one conflict.
Scroll away/back and confirm first/last choices. Offscreen checks do not replace
physical keyboard acceptance. Record the
Windows build, scaling and observations beside the JSON reports. The lifecycle
script does not claim these manual GUI checks passed.

Return C:\FmaReports, plus the exact OS version and any GUI observations.
The scripts never scan personal files. Generated sources and application data
stay in the retained report tree, except for the deliberate default-data sentinel
used to verify that uninstall preserves user data.

{{SOURCE}} and its checksum contain the pinned native wrapper, dependencies,
notices and offline rebuild instructions. Preserve that source archive alongside
the binary artifacts. See app\Licenses\Rust\SOURCE.json for its exact checksum.
The managed engine remains the default; both engines remain Experimental.
'@
$readme = $readme.Replace('{{CONFIGURATION}}', $Configuration).Replace('{{PORTABLE}}', $portable).Replace('{{INSTALLER}}', $installer).Replace('{{SOURCE}}', $source)
[IO.File]::WriteAllText((Join-Path $kit 'README.md'), $readme.Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
$lines = foreach ($file in (Get-ChildItem -LiteralPath $kit -File -Recurse | Sort-Object FullName)) {
    (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $file.FullName.Substring($kit.Length + 1)
}
[IO.File]::WriteAllLines((Join-Path $kit 'CHECKSUMS.sha256'), $lines, [Text.UTF8Encoding]::new($false))
Write-Host "Validation kit: $kit"
