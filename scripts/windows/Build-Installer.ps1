<#
.SYNOPSIS
    Build the File Workbench setup program (NSIS) for Windows x64.

.DESCRIPTION
    Runs the Core tests, publishes the GUI (self-contained, win-x64) and the file-workbench command-line tool (self-contained
    single file), stages the pinned native adapter with its rebuild source, collects runtime/NuGet/Cargo notices, and compiles
    installer/FileWorkbench.nsi with NSIS.

    The version, publisher and update repository come from Directory.Build.props. makensis.exe is taken from
    -NsisPath, PATH or Program Files; otherwise the pinned portable NSIS archive is downloaded into build/tools and
    its SHA-256 is checked. Native-by-default builds also need rustup, pinned Rust 1.99.1,
    and the MSVC x64 C++/Windows SDK build tools. End-user machines need none of these SDKs.

    Results go to build/ (each build/installer/run-<id> work folder is kept for inspection):
      Release: FileWorkbench-<version>-x64-setup.exe, its .sha256, and file-workbench-update-windows.json,
               the manifest the in-app update check reads from the GitHub release.
      Debug:   FileWorkbench-<version>-x64-debug-setup.exe and its .sha256. No update manifest is written, so a
               Debug build can never be offered as an update.

    This preview omits ImageSharp and its legacy media algorithms; native image
    analysis remains available. See docs/deployment.md for package requirements.
    Nothing is signed, installed or published.

.PARAMETER Configuration
    Release (default) or Debug.

.PARAMETER UpdateSummary
    Plain-text summary (at most 500 characters) for the update manifest. Defaults to the summary of this version in
    docs/release-notes.md. Release only.

.PARAMETER NsisPath
    Full path of makensis.exe to use instead of searching for it.

.PARAMETER NoDownload
    Fail instead of downloading NSIS when makensis.exe is not found.

.PARAMETER UsePinnedNsis
    Always use the pinned portable NSIS (downloaded and checked if needed), ignoring any installed copy. The release
    workflow uses this so that builds do not depend on the NSIS a build machine happens to have.

.PARAMETER SkipTests
    Skip the Core tests, for example when they just ran.

.PARAMETER WithoutNative
    Explicit managed-only package. The optional native engine remains unavailable; no runtime fallback is added.

.EXAMPLE
    .\scripts\windows\Build-Installer.ps1 -Configuration Debug

.EXAMPLE
    .\scripts\windows\Build-Installer.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [Parameter(Mandatory = $false)]
    [string]$UpdateSummary,

    [Parameter(Mandatory = $false)]
    [string]$NsisPath,

    [Parameter(Mandatory = $false)]
    [switch]$NoDownload,

    [Parameter(Mandatory = $false)]
    [switch]$UsePinnedNsis,

    [Parameter(Mandatory = $false)]
    [switch]$SkipTests,

    [switch]$WithoutNative
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# Portable NSIS used when no makensis.exe is installed. The MD5 published by SourceForge for this file
# (530e3623c077cba879ed2ae6e13be362) matched the download that this SHA-256 was taken from.
$NsisVersion = '3.13'
$NsisSha256 = 'ba63dffc4410ee89193e1cb5a41989991bd77c61068da17e3156d136b7b0b3d8'
$NsisUrl = "https://sourceforge.net/projects/nsis/files/NSIS%203/$NsisVersion/nsis-$NsisVersion.zip/download"

function Invoke-Checked([string]$Description, [scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Description failed (exit code $LASTEXITCODE)." }
}

function Write-Checksum([string]$Path) {
    $Hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$Path.sha256", "$Hash  $(Split-Path -Leaf $Path)`n", [Text.UTF8Encoding]::new($false))
    return $Hash
}

function Get-ProjectProperties {
    $Json = & dotnet msbuild (Join-Path $ProjectRoot 'src/Gui/Gui.csproj') -nologo `
        -getProperty:Version -getProperty:Company -getProperty:UpdateRepository
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the version from Directory.Build.props.' }
    $Properties = ($Json -join "`n" | ConvertFrom-Json).Properties
    if ($Properties.Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw "Version '$($Properties.Version)' in Directory.Build.props must be x.y.z."
    }
    if ($Properties.UpdateRepository -notmatch '^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9._-]+$') {
        throw "UpdateRepository '$($Properties.UpdateRepository)' in Directory.Build.props must be owner/name."
    }
    # makensis would expand $ and break on quotes in /D values.
    if (-not $Properties.Company -or $Properties.Company -match '[$"]') {
        throw "Company '$($Properties.Company)' in Directory.Build.props must be set and contain no `$ or quote."
    }
    return $Properties
}

function Resolve-Makensis {
    if ($NsisPath) {
        if (-not (Test-Path -LiteralPath $NsisPath -PathType Leaf)) { throw "makensis.exe not found at $NsisPath." }
        return (Resolve-Path -LiteralPath $NsisPath).Path
    }
    if (-not $UsePinnedNsis) {
        $OnPath = Get-Command 'makensis.exe' -ErrorAction SilentlyContinue
        if ($OnPath) { return $OnPath.Source }
        foreach ($Base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
            if ($Base) {
                $Candidate = Join-Path $Base 'NSIS\makensis.exe'
                if (Test-Path -LiteralPath $Candidate -PathType Leaf) { return $Candidate }
            }
        }
    }
    $ToolRoot = Join-Path $ProjectRoot 'build/tools'
    $Local = Join-Path $ToolRoot "nsis-$NsisVersion/makensis.exe"
    if (Test-Path -LiteralPath $Local -PathType Leaf) { return $Local }
    if ($NoDownload) {
        throw "makensis.exe was not found. Install NSIS 3, pass -NsisPath, or run without -NoDownload to fetch NSIS $NsisVersion."
    }

    New-Item -ItemType Directory -Path $ToolRoot -Force | Out-Null
    $Archive = Join-Path $ToolRoot "nsis-$NsisVersion.zip"
    # Write-Host keeps the message out of this function's return value.
    Write-Host "Downloading NSIS $NsisVersion..."
    # SourceForge answers browsers with an HTML page, so a plain client user agent is sent to get the file itself.
    Invoke-WebRequest -Uri $NsisUrl -OutFile $Archive -UseBasicParsing -UserAgent 'Wget' -MaximumRedirection 10
    $Actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $Archive).Hash.ToLowerInvariant()
    if ($Actual -ne $NsisSha256) {
        Remove-Item -LiteralPath $Archive -Force
        throw "The NSIS download has SHA-256 $Actual, expected $NsisSha256. It was deleted."
    }
    Expand-Archive -LiteralPath $Archive -DestinationPath $ToolRoot -Force
    Remove-Item -LiteralPath $Archive -Force
    if (-not (Test-Path -LiteralPath $Local -PathType Leaf)) { throw "The NSIS archive did not contain $Local." }
    return $Local
}


# Number of real files in a project.assets.json asset group; "_._" marks an intentionally empty folder.
function Get-AssetCount($Group) {
    if ($null -eq $Group) { return 0 }
    return @($Group.PSObject.Properties | Where-Object { $_.Name -notlike '*/_._' }).Count
}

function Copy-LicenseNotices([string]$AppStage, [string[]]$AssetsFiles) {
    $LicenseRoot = Join-Path $AppStage 'Licenses'
    New-Item -ItemType Directory -Path $LicenseRoot -Force | Out-Null
    $Lines = [Collections.Generic.List[string]]::new()
    $Lines.Add('Third-party software included in File Workbench')
    $Lines.Add('=======================================================')
    $Lines.Add('')
    $Lines.Add('Each folder here holds the license and notice files shipped in that package. License names are')
    $Lines.Add('SPDX identifiers; their full texts are at https://spdx.org/licenses/.')
    $Lines.Add('')

    # The bundled .NET runtimes: their license and third-party notices come from the runtime packs.
    $RuntimeConfig = Get-Content -LiteralPath (Join-Path $AppStage 'FileWorkbench.runtimeconfig.json') -Raw | ConvertFrom-Json
    $PackagesRoot = (& dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''
    foreach ($Framework in $RuntimeConfig.runtimeOptions.includedFrameworks) {
        $PackageId = switch ($Framework.name) {
            'Microsoft.NETCore.App' { 'microsoft.netcore.app.runtime.win-x64' }
            'Microsoft.WindowsDesktop.App' { 'microsoft.windowsdesktop.app.runtime.win-x64' }
            default { $null }
        }
        if (-not $PackageId) { continue }
        $Folder = Join-Path $PackagesRoot.Trim() "$PackageId/$($Framework.version)"
        $Target = Join-Path $LicenseRoot "$($Framework.name)"
        New-Item -ItemType Directory -Path $Target -Force | Out-Null
        foreach ($Name in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
            $Source = Join-Path $Folder $Name
            if (Test-Path -LiteralPath $Source) { Copy-Item -LiteralPath $Source -Destination $Target }
        }
        $Lines.Add("$($Framework.name) $($Framework.version) - MIT (see $($Framework.name)\)")
    }

    # NuGet packages that the GUI and the CLI ship, read from their restore results. Only packages with runtime or
    # native files for win-x64 end up in the output; build-only packages such as analyzers are skipped.
    $Seen = @{}
    foreach ($AssetsFile in $AssetsFiles) {
        $Assets = Get-Content -LiteralPath $AssetsFile -Raw | ConvertFrom-Json
        $Folders = @($Assets.packageFolders.PSObject.Properties.Name)
        $RuntimeTarget = $Assets.targets.PSObject.Properties | Where-Object { $_.Name -like '*/win-x64' } | Select-Object -First 1
        if (-not $RuntimeTarget) { throw "$AssetsFile has no win-x64 target; publish with --runtime win-x64 first." }
        foreach ($Library in $Assets.libraries.PSObject.Properties) {
            if ($Library.Value.type -ne 'package' -or $Seen.ContainsKey($Library.Name)) { continue }
            $Target = $RuntimeTarget.Value.($Library.Name)
            if (-not $Target -or ((Get-AssetCount $Target.runtime) + (Get-AssetCount $Target.native)) -eq 0) { continue }
            $Seen[$Library.Name] = $true
            $PackageFolder = $null
            foreach ($Root in $Folders) {
                $Candidate = Join-Path $Root $Library.Value.path
                if (Test-Path -LiteralPath $Candidate) { $PackageFolder = $Candidate; break }
            }
            if (-not $PackageFolder) { throw "Package folder not found for $($Library.Name)." }
            $License = 'see package'
            $Nuspec = Get-ChildItem -LiteralPath $PackageFolder -Filter '*.nuspec' -File | Select-Object -First 1
            if ($Nuspec) {
                [xml]$Spec = Get-Content -LiteralPath $Nuspec.FullName -Raw
                $Metadata = $Spec.package.metadata
                $LicenseFile = $null
                if ($Metadata.license -and $Metadata.license.type -eq 'expression') { $License = $Metadata.license.'#text' }
                elseif ($Metadata.license -and $Metadata.license.type -eq 'file') {
                    $LicenseFile = Join-Path $PackageFolder $Metadata.license.'#text'
                    $License = "license file $(Split-Path -Leaf $LicenseFile)"
                }
                elseif ($Metadata.licenseUrl) { $License = $Metadata.licenseUrl }
            }
            $Notices = @(Get-ChildItem -LiteralPath $PackageFolder -File | Where-Object {
                $_.Name -match '^(LICENSE|LICENCE|NOTICE|THIRD-PARTY-NOTICES|ThirdPartyNotices)(\.[A-Za-z]+)?$'
            })
            if ($LicenseFile -and (Test-Path -LiteralPath $LicenseFile -PathType Leaf) -and
                -not ($Notices | Where-Object { $_.FullName -eq (Resolve-Path -LiteralPath $LicenseFile).Path })) {
                $Notices += Get-Item -LiteralPath $LicenseFile
            }
            if ($Library.Name -eq 'System.IO.Hashing/10.0.13') {
                $Supplement = Join-Path $ProjectRoot 'third-party-notices/system.io.hashing-10.0.13'
                $NoticeSource = Get-Content -LiteralPath (Join-Path $Supplement 'SOURCE.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                if (-not $Nuspec -or $Metadata.id -ne $NoticeSource.package -or $Metadata.version -ne $NoticeSource.version -or
                    $Metadata.repository.commit -ne $NoticeSource.commit -or
                    (Get-FileHash -LiteralPath (Join-Path $Supplement $NoticeSource.file) -Algorithm SHA256).Hash -ne $NoticeSource.sha256) {
                    throw 'Pinned System.IO.Hashing notice provenance/checksum mismatch.'
                }
                $Notices += Get-Item -LiteralPath (Join-Path $Supplement $NoticeSource.file), (Join-Path $Supplement 'SOURCE.json')
            }
            if ($Notices.Count -gt 0) {
                $Target = Join-Path $LicenseRoot ($Library.Name -replace '/', ' ')
                New-Item -ItemType Directory -Path $Target -Force | Out-Null
                $Notices | Copy-Item -Destination $Target
            }
            $Lines.Add("$($Library.Name -replace '/', ' ') - $License")
        }
    }
    [IO.File]::WriteAllLines((Join-Path $LicenseRoot 'README.txt'), $Lines, [Text.UTF8Encoding]::new($false))
}

if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64) {
    throw 'This script builds the Windows x64 setup program; run it on Windows x64.'
}
if (-not (Get-Command 'dotnet' -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is required (see global.json for the version).'
}
if ($UsePinnedNsis -and $NsisPath) { throw 'Use either -UsePinnedNsis or -NsisPath, not both.' }
if ($UpdateSummary -and $Configuration -ne 'Release') { throw 'UpdateSummary applies to Release builds only.' }
if ($UpdateSummary.Length -gt 500) { throw 'UpdateSummary must be at most 500 characters.' }
Push-Location $ProjectRoot
$PreviousTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$PreviousNoLogo = $env:DOTNET_NOLOGO
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $Properties = Get-ProjectProperties
    $Version = $Properties.Version
    $Suffix = if ($Configuration -eq 'Release') { '' } else { '-' + $Configuration.ToLowerInvariant() }
    Write-Output "File Workbench $Version ($Configuration)"

    if (-not $SkipTests) {
        Invoke-Checked 'Core tests' {
            dotnet test tests/Core.Tests/Core.Tests.csproj --configuration $Configuration --verbosity minimal `
                --blame-hang-timeout 60s --blame-hang-dump-type none
        }
    }

    $BuildRoot = [IO.Path]::GetFullPath((Join-Path $ProjectRoot 'build'))
    $WorkParent = Join-Path $BuildRoot 'installer'
    foreach ($Probe in @($BuildRoot, $WorkParent)) {
        if ((Test-Path -LiteralPath $Probe) -and ((Get-Item -LiteralPath $Probe -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "The build output must not traverse a reparse point: $Probe"
        }
    }
    $Work = [IO.Path]::GetFullPath((Join-Path $WorkParent ('run-' + [Guid]::NewGuid().ToString('N'))))
    if (-not $Work.StartsWith($BuildRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid installer work directory.' }
    $Stage = Join-Path $Work 'stage'
    $AppStage = Join-Path $Stage 'app'
    $CliStage = Join-Path $Stage 'cli'
    $CliPublish = Join-Path $Work 'cli-publish'
    New-Item -ItemType Directory -Path $AppStage, $CliStage -Force | Out-Null

    Invoke-Checked 'GUI publish' {
        dotnet publish src/Gui/Gui.csproj --configuration $Configuration --runtime win-x64 --self-contained true `
            --output $AppStage
    }
    Invoke-Checked 'CLI publish' {
        dotnet publish src/Cli/Cli.csproj --configuration $Configuration --runtime win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
            --output $CliPublish
    }
    Copy-Item -LiteralPath (Join-Path $CliPublish 'FileWorkbench.Cli.exe') -Destination (Join-Path $CliStage 'file-workbench.exe')

    # The CLI shares the mandatory sibling app payload only when both package folders are marked.
    foreach ($PackageDirectory in @($AppStage, $CliStage)) {
        [IO.File]::WriteAllText((Join-Path $PackageDirectory '.installed-by-file-merge-assistant-setup'),
            "Owned File Workbench package directory.`n", [Text.UTF8Encoding]::new($false))
    }

    Copy-Item -LiteralPath (Join-Path $ProjectRoot 'LICENSE') -Destination (Join-Path $AppStage 'LICENSE.txt')
    Copy-LicenseNotices $AppStage @(
        (Join-Path $ProjectRoot 'src/Gui/obj/project.assets.json'),
        (Join-Path $ProjectRoot 'src/Cli/obj/project.assets.json'))

    $NativeSourceArchive = $null
    if (-not $WithoutNative) {
        $SourceKit = Join-Path $Work 'native-source'
        & (Join-Path $PSScriptRoot 'Export-NativeAnalysis.ps1') -Destination $AppStage -SourceKitRoot $SourceKit `
            -Configuration Release -AllowIncompleteNotices:($Configuration -eq 'Debug')
        $NativeManifest = Get-Content -LiteralPath (Join-Path $AppStage 'runtimes/win-x64/native/native-manifest.json') -Raw | ConvertFrom-Json
        $NativeSourceArchive = Join-Path $Work "FileWorkbench-$Version-czkawka-$($NativeManifest.engineVersion)$Suffix-source.zip"
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($SourceKit, $NativeSourceArchive, [IO.Compression.CompressionLevel]::Optimal, $false)
        $SourceSha = Write-Checksum $NativeSourceArchive
        $SourceReference = [ordered]@{ formatVersion = 1; archive = (Split-Path -Leaf $NativeSourceArchive); sha256 = $SourceSha }
        [IO.File]::WriteAllText((Join-Path $AppStage 'Licenses/Rust/SOURCE.json'),
            ($SourceReference | ConvertTo-Json) + "`n", [Text.UTF8Encoding]::new($false))
        & (Join-Path $PSScriptRoot 'Test-AnalysisPackage.ps1') -PackageRoot $Stage `
            -FixturesRoot (Join-Path $ProjectRoot 'tests/Fixtures') -ReportParent (Join-Path $Work 'verification')
    }

    $Makensis = Resolve-Makensis
    $NsisReported = (& $Makensis /VERSION) -join ''
    if ($NsisReported -notmatch '^v?3\.') { throw "NSIS 3 is required; $Makensis reports '$NsisReported'." }

    $SetupName = "FileWorkbench-$Version-x64$Suffix-setup.exe"
    $Setup = Join-Path $Work $SetupName
    Invoke-Checked 'NSIS compile' {
        & $Makensis /V2 /INPUTCHARSET UTF8 "/DVERSION=$Version" "/DCONFIGURATION=$Configuration" "/DSTAGE=$Stage" `
            "/DOUTFILE=$Setup" "/DPUBLISHER=$($Properties.Company)" "/DREPOSITORY=$($Properties.UpdateRepository)" `
            (Join-Path $ProjectRoot 'installer/FileWorkbench.nsi')
    }
    $Sha256 = Write-Checksum $Setup
    $Delivered = @($Setup, "$Setup.sha256")
    if ($NativeSourceArchive) { $Delivered += @($NativeSourceArchive, "$NativeSourceArchive.sha256") }

    # Keep app/ and cli/ together: the CLI resolves the same package-owned adapter as the GUI.
    $Portable = Join-Path $Work "FileWorkbench-$Version-x64$Suffix-portable.zip"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($Stage, $Portable, [IO.Compression.CompressionLevel]::Optimal, $false)
    $null = Write-Checksum $Portable
    $Delivered += @($Portable, "$Portable.sha256")

    if ($Configuration -eq 'Release') {
        if (-not $PSBoundParameters.ContainsKey('UpdateSummary')) {
            # Fails when docs/release-notes.md has no entry or summary for this version.
            $UpdateSummary = (& (Join-Path $PSScriptRoot 'Get-ReleaseNotes.ps1') -Version $Version -Summary) -join ' '
        }
        if ($UpdateSummary.Length -gt 500) { throw 'The update summary must be at most 500 characters.' }
        $Manifest = Join-Path $Work 'file-workbench-update-windows.json'
        $Update = [ordered]@{
            schemaVersion = 1
            platform = 'windows'
            version = $Version
            tag = "v$Version"
            summary = [string]$UpdateSummary
            assets = @([ordered]@{ arch = 'x64'; name = $SetupName; size = (Get-Item -LiteralPath $Setup).Length; sha256 = $Sha256 })
        }
        [IO.File]::WriteAllText($Manifest, ($Update | ConvertTo-Json -Depth 4) + "`n", [Text.UTF8Encoding]::new($false))
        $Delivered += $Manifest
    }

    # Only a complete set replaces earlier results in build/. A Debug build also removes a Release manifest left
    # from an earlier build, so a manifest never sits next to a setup program it does not describe.
    $Delivery = Join-Path $ProjectRoot 'build'
    $OldManifest = Join-Path $Delivery 'file-workbench-update-windows.json'
    if ($Configuration -ne 'Release' -and (Test-Path -LiteralPath $OldManifest)) { Remove-Item -LiteralPath $OldManifest -Force }
    Move-Item -LiteralPath $Delivered -Destination $Delivery -Force
    foreach ($File in $Delivered) { Write-Output "Built: $(Join-Path $Delivery (Split-Path -Leaf $File))" }
    if ($Configuration -ne 'Release') {
        Write-Output 'Debug build: not optimized, and no update manifest was written. Do not publish it as a release.'
    }
    Write-Output 'The setup program is not signed; Windows SmartScreen may warn before it runs.'
} finally {
    Pop-Location
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $PreviousTelemetry
    $env:DOTNET_NOLOGO = $PreviousNoLogo
}
