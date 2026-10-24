<#
.SYNOPSIS
    Stage the pinned native adapter, notices and complete rebuild source for a local package.
.DESCRIPTION
    Builds the Windows x64 adapter with its pinned static CRT configuration, copies
    only its real DLL into the application's controlled runtime folder, inventories
    resolved non-development Cargo dependencies, and vendors the locked source.
    Does not install, sign or publish anything. Outputs must be under this repo's build folder.
.PARAMETER Destination
    Published application directory under build/.
.PARAMETER SourceKitRoot
    New directory under build/ for the native rebuild source; it must not exist.
.PARAMETER Configuration
    Native Debug or Release profile. Independent of the .NET application configuration.
.PARAMETER AllowIncompleteNotices
    Internal Debug-package inspection only. Retain explicit missing-notice diagnostics;
    Release application packaging must never pass this switch.
.EXAMPLE
    .\scripts\windows\Export-NativeAnalysis.ps1 -Destination C:\repo\build\stage\app -SourceKitRoot C:\repo\build\native-source
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][string]$SourceKitRoot,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$AllowIncompleteNotices
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
$buildRoot = Join-Path $projectRoot 'build'
$adapterRoot = Join-Path $projectRoot 'native/fma-czkawka-ffi'
$target = 'x86_64-pc-windows-msvc'

function Assert-BuildPath([string]$Path) {
    $absolute = [IO.Path]::GetFullPath($Path)
    if (-not $absolute.StartsWith($buildRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Native package outputs must be inside this repository build directory.'
    }
    $probe = $absolute
    while ($probe -and $probe.Length -ge $buildRoot.Length) {
        if (Test-Path -LiteralPath $probe) {
            if ((Get-Item -LiteralPath $probe -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Package output traverses a reparse point: $probe"
            }
        }
        $probe = Split-Path -Parent $probe
    }
    return $absolute
}

$Destination = Assert-BuildPath $Destination
$SourceKitRoot = Assert-BuildPath $SourceKitRoot
if (Test-Path -LiteralPath $SourceKitRoot) { throw 'SourceKitRoot already exists; use a new build work directory.' }
& (Join-Path $PSScriptRoot 'Build-NativeAnalysis.ps1') -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }

Push-Location $adapterRoot
try {
    $raw = & cargo +1.99.1 metadata --locked --offline --format-version 1 --filter-platform $target
    if ($LASTEXITCODE -ne 0) { throw 'Cargo metadata failed.' }
    $metadata = ($raw -join "`n") | ConvertFrom-Json
    $rustVersion = (& rustc +1.99.1 --version) -join ' '
    if ($LASTEXITCODE -ne 0) { throw 'rustc version query failed.' }
    $sysroot = ((& rustc +1.99.1 --print sysroot) -join '').Trim()
    if ($LASTEXITCODE -ne 0) { throw 'rustc sysroot query failed.' }
} finally { Pop-Location }

$packageById = @{}
foreach ($package in $metadata.packages) { $packageById[$package.id] = $package }
$nodeById = @{}
foreach ($node in $metadata.resolve.nodes) { $nodeById[$node.id] = $node }
$pending = [Collections.Generic.Queue[string]]::new()
$pending.Enqueue($metadata.resolve.root)
$reachable = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
while ($pending.Count -gt 0) {
    $id = $pending.Dequeue()
    if (-not $reachable.Add($id)) { continue }
    foreach ($dependency in $nodeById[$id].deps) {
        if (@($dependency.dep_kinds | Where-Object { $_.kind -ne 'dev' }).Count -gt 0) { $pending.Enqueue($dependency.pkg) }
    }
}

$nativeDirectory = Join-Path $Destination 'runtimes/win-x64/native'
$noticeRoot = Join-Path $Destination 'Licenses/Rust'
New-Item -ItemType Directory -Path $nativeDirectory, $noticeRoot -Force | Out-Null
$sourceLibrary = Join-Path $adapterRoot "target/$target/$($Configuration.ToLowerInvariant())/fma_czkawka_ffi.dll"
Copy-Item -LiteralPath $sourceLibrary -Destination $nativeDirectory -Force
Copy-Item -LiteralPath (Join-Path $adapterRoot 'THIRD-PARTY-NOTICES.md') -Destination $noticeRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $noticeRoot 'FMA-LICENSE.txt')
$packages = [Collections.Generic.List[object]]::new()
$missingNotices = [Collections.Generic.List[string]]::new()
$curatedRoot = Join-Path $adapterRoot 'third-party-notices'
$curated = Get-Content -LiteralPath (Join-Path $curatedRoot 'sources.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($curated.formatVersion -ne 1) { throw 'Unsupported curated notice manifest.' }
foreach ($id in ($reachable | Sort-Object)) {
    $package = $packageById[$id]
    if ($id -eq $metadata.resolve.root) { continue }
    $packageDirectory = Split-Path -Parent $package.manifest_path
    $notices = @(Get-ChildItem -LiteralPath $packageDirectory -File -Recurse | Where-Object {
        $_.Name -match '(?i)licen[cs]e|copying|notice|copyright' -or $_.DirectoryName -match '(?i)[\\/]licen[cs]es[\\/]'
    })
    if ($package.license_file) {
        $declared = if ([IO.Path]::IsPathRooted($package.license_file)) { $package.license_file }
            else { [IO.Path]::GetFullPath((Join-Path $packageDirectory $package.license_file)) }
        if ((Test-Path -LiteralPath $declared -PathType Leaf) -and $declared -notin $notices.FullName) { $notices += Get-Item -LiteralPath $declared }
    }
    $relative = "$($package.name)-$($package.version)"
    $folder = Join-Path $noticeRoot $relative
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $noticeNames = @()
    foreach ($notice in $notices) {
        $name = if ($notice.FullName.StartsWith($packageDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            $notice.FullName.Substring($packageDirectory.Length + 1)
        } else { 'declared/' + $notice.Name }
        $output = Join-Path $folder $name
        New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
        Copy-Item -LiteralPath $notice.FullName -Destination $output -Force
        $noticeNames += $name.Replace('\', '/')
    }
    $fallback = @($curated.packages | Where-Object { $_.name -eq $package.name -and $_.version -eq $package.version })
    if ($fallback.Count -gt 1) { throw 'Duplicate curated dependency notices.' }
    foreach ($entry in $fallback) {
        $vcs = Get-Content -LiteralPath (Join-Path $packageDirectory '.cargo_vcs_info.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($vcs.git.sha1 -ne $entry.commit) { throw 'Curated notice commit differs from the pinned crate provenance.' }
        foreach ($file in $entry.files) {
            $inputPath = [IO.Path]::GetFullPath((Join-Path $curatedRoot $file.path))
            if (-not $inputPath.StartsWith($curatedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
                (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { throw 'Curated native notice path/checksum mismatch.' }
            $name = 'upstream/' + $file.upstreamPath
            $output = [IO.Path]::GetFullPath((Join-Path $folder $name))
            if (-not $output.StartsWith($folder + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid notice output path.' }
            New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
            Copy-Item -LiteralPath $inputPath -Destination $output -Force
            $noticeNames += $name
        }
        [IO.File]::WriteAllText((Join-Path $folder 'UPSTREAM-SOURCES.json'), ($entry | ConvertTo-Json -Depth 8) + "`n", [Text.UTF8Encoding]::new($false))
    }
    if ($noticeNames.Count -eq 0) {
        $missingNotices.Add("$($package.name) $($package.version)")
        # Preserve the actual declaration and attribution; do not invent missing upstream notice text.
        Copy-Item -LiteralPath $package.manifest_path -Destination (Join-Path $folder 'UPSTREAM-Cargo.toml')
        [IO.File]::WriteAllText((Join-Path $folder 'MISSING-NOTICE.txt'),
            "Upstream declares $($package.license), but no standalone notice text was found for this pinned package. Release packaging is blocked pending resolution.`n", [Text.UTF8Encoding]::new($false))
    }
    $packages.Add([ordered]@{
        name = $package.name; version = $package.version; source = $package.source
        license = $package.license; noticeDirectory = $relative
        noticeFiles = $noticeNames; noticeStatus = $(if ($noticeNames.Count -gt 0) { 'Collected' } else { 'DeclaredOnly' })
    })
}
if ($missingNotices.Count -gt 0) {
    $problem = 'Missing standalone native dependency notices: ' + ($missingNotices -join ', ')
    if (-not $AllowIncompleteNotices) { throw "$problem. Release packaging is blocked; see third-party-notices/README.md." }
    Write-Warning "$problem. This internal Debug inspection package is not release-ready."
}
$rustNotices = Join-Path $noticeRoot 'Rust-standard-library'
New-Item -ItemType Directory -Path $rustNotices -Force | Out-Null
foreach ($name in @('COPYRIGHT-library.html', 'COPYRIGHT.html', 'README.md')) {
    $source = Join-Path $sysroot "share/doc/rust/$name"
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Pinned Rust compiler distribution notice is missing: $name" }
    Copy-Item -LiteralPath $source -Destination $rustNotices
}

# Preserve crate layout so include_str! test fixtures work after extraction.
$sourceCrate = Join-Path $SourceKitRoot 'native/fma-czkawka-ffi'
$sourceConfig = Join-Path $sourceCrate '.cargo'
New-Item -ItemType Directory -Path $sourceConfig -Force | Out-Null
foreach ($name in @('Cargo.toml', 'Cargo.lock', 'rust-toolchain.toml', 'rustfmt.toml', 'README.md', 'ABI.md', 'THIRD-PARTY-NOTICES.md', 'third-party-notices', 'src')) {
    Copy-Item -LiteralPath (Join-Path $adapterRoot $name) -Destination $sourceCrate -Recurse
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $SourceKitRoot
$fixtures = Join-Path $SourceKitRoot 'tests/Fixtures'
New-Item -ItemType Directory -Path $fixtures -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'tests/Fixtures/analysis') -Destination $fixtures -Recurse
$vendorDirectory = Join-Path $SourceKitRoot 'vendor'
Push-Location $adapterRoot
try {
    # Cargo handles checksums and source replacement for the exact lockfile, including the Git dependency.
    $vendorLog = Join-Path (Split-Path -Parent $SourceKitRoot) 'native-vendor.log'
    $vendorConfig = & cargo +1.99.1 vendor --locked --versioned-dirs $vendorDirectory 2> $vendorLog
    if ($LASTEXITCODE -ne 0) { throw "Cargo source vendoring failed; inspect $vendorLog." }
} finally { Pop-Location }
$portableConfig = ($vendorConfig -join "`n").Replace($vendorDirectory.Replace('\', '/'), '../../vendor').Replace($vendorDirectory.Replace('\', '\\'), '../../vendor')
$pinnedConfig = Get-Content -LiteralPath (Join-Path $adapterRoot '.cargo/config.toml') -Raw
[IO.File]::WriteAllText((Join-Path $sourceConfig 'config.toml'), $pinnedConfig.TrimEnd() + "`n`n" + $portableConfig + "`n", [Text.UTF8Encoding]::new($false))

$readme = @'
# Native rebuild source

This archive contains FMA's native wrapper, its pinned Cargo dependencies and test
fixture. DEPENDENCIES.json lists the resolved non-development
dependency licenses; the vendor tree also includes build/test/platform sources.
noticeCollectionComplete=false records unresolved upstream notice text; an
internal Debug inspection archive does not authorize public redistribution.
Czkawka core's MIT license is separate from dependency licenses (including rawler
LGPL-2.1 and symphonia MPL-2.0). Preserve their notices and source when distributing.

On Windows x64, install the pinned Rust toolchain and MSVC/Windows SDK build tools,
then run from native/fma-czkawka-ffi:

    $buildTarget = [IO.Path]::GetFullPath((Join-Path (Get-Location) '../../../target'))
    cargo build --locked --offline --release --target x86_64-pc-windows-msvc --target-dir $buildTarget
    cargo test --locked --offline --target x86_64-pc-windows-msvc --target-dir $buildTarget

Use a short extraction path if MSVC reports LNK1104 or a path-length failure.
Use a normalized absolute target directory, rather than one containing '..'.

Replace app/runtimes/win-x64/native/fma_czkawka_ffi.dll in a closed local package
with the rebuilt DLL. ABI/version checks still apply. The source supports rebuild
and modification; no bit-identical binary guarantee is made. End users running
the packaged app do not need this compiler toolchain.
'@
[IO.File]::WriteAllText((Join-Path $SourceKitRoot 'REBUILD.md'), $readme.Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))

$sha = (Get-FileHash -LiteralPath (Join-Path $nativeDirectory 'fma_czkawka_ffi.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    formatVersion = 1; engine = 'czkawka'; engineVersion = $packageById[$metadata.resolve.root].version
    abiVersion = 1; target = $target; profile = $Configuration; rust = $rustVersion; staticCrt = $true
    upstreamCommit = 'eb8b91dbb4d25dff416202674ee0de84ee4b5e5c'; sha256 = $sha
    cargoLockSha256 = (Get-FileHash -LiteralPath (Join-Path $adapterRoot 'Cargo.lock') -Algorithm SHA256).Hash.ToLowerInvariant()
    noticeCollectionComplete = ($missingNotices.Count -eq 0); missingNotices = @($missingNotices)
    packages = @($packages)
}
$json = ($manifest | ConvertTo-Json -Depth 8) + "`n"
[IO.File]::WriteAllText((Join-Path $nativeDirectory 'native-manifest.json'), $json, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $noticeRoot 'DEPENDENCIES.json'), $json, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $SourceKitRoot 'DEPENDENCIES.json'), $json, [Text.UTF8Encoding]::new($false))
Write-Host "Staged native adapter and $($packages.Count) dependency records; rebuild source: $SourceKitRoot"
