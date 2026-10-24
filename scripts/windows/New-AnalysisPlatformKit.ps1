<#
.SYNOPSIS
    Prepare private arm64 macOS/Linux analysis verification payloads and source.
.DESCRIPTION
    Publishes the net10.0 verifier only (no Windows Core/WPF/ImageSharp), includes
    its source/tests, merges the checksum-verified pinned native rebuild source,
    and writes target scripts/notices/checksums. Does not compile foreign native
    libraries, install, publish, run remote hosts or declare platform support.
.PARAMETER NativeSourceArchive
    Existing native Debug inspection source ZIP with its adjacent .sha256 file.
.EXAMPLE
    .\scripts\windows\New-AnalysisPlatformKit.ps1
#>
[CmdletBinding()]
param([string]$NativeSourceArchive)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$build = Join-Path $root 'build'
if ((Get-Item -LiteralPath $build -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'build/ must not be a reparse point.' }
if (-not $NativeSourceArchive) {
    [xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw -Encoding UTF8
    $version = @($props.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    $crate = Get-Content -LiteralPath (Join-Path $root 'native/file-workbench-czkawka-ffi/Cargo.toml') -Raw -Encoding UTF8
    $nativeVersion = [regex]::Match($crate, '(?m)^version = "(\d+\.\d+\.\d+)"').Groups[1].Value
    $NativeSourceArchive = Join-Path $build "FileWorkbench-$version-czkawka-$nativeVersion-debug-source.zip"
}
$NativeSourceArchive = [IO.Path]::GetFullPath($NativeSourceArchive)
$expected = ((Get-Content -LiteralPath "$NativeSourceArchive.sha256" -Raw) -split '\s+')[0]
if ($expected -notmatch '^[0-9a-fA-F]{64}$' -or (Get-FileHash -LiteralPath $NativeSourceArchive -Algorithm SHA256).Hash -ne $expected) { throw 'Native source archive checksum mismatch.' }
$kit = Join-Path $build ('analysis-platform-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $kit | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($NativeSourceArchive, $kit)
# The original inventory is Windows-specific. The target runner captures its own Cargo graph.
Move-Item -LiteralPath (Join-Path $kit 'DEPENDENCIES.json') -Destination (Join-Path $kit 'DEPENDENCIES.windows.json')
foreach ($file in @('Directory.Build.props', 'global.json')) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $kit }
foreach ($directory in @('src/analysis', 'tests/analysis-verifier', 'tests/analysis-tests')) {
    $target = Join-Path $kit $directory
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $root $directory) -File | Where-Object { $_.Extension -in @('.cs', '.csproj') } | Copy-Item -Destination $target
}
foreach ($relative in @('Analysis/ManagedAnalysisEngineTests.cs', 'Analysis/AnalysisResultValidatorTests.cs', 'Analysis/NativeAnalysisPathsTests.cs', 'Analysis/KnownAnswerDataset.cs', 'Scanning/TempTree.cs')) {
    $target = Join-Path $kit "tests/Core.Tests/$relative"
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root "tests/Core.Tests/$relative") -Destination $target
}
Copy-Item -LiteralPath (Join-Path $root 'tests/Fixtures/similar-images') -Destination (Join-Path $kit 'tests/Fixtures') -Recurse
$scriptTarget = Join-Path $kit 'scripts/testing'
New-Item -ItemType Directory -Path $scriptTarget -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'scripts/testing/verify-analysis-platform.sh') -Destination $scriptTarget
$packages = ((& dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', '').Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not locate .NET package notices.' }
$licenses = Join-Path $kit 'Licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
foreach ($rid in @('win-x64', 'linux-arm64', 'osx-arm64')) {
    $output = Join-Path $kit "bin/$rid"
    & dotnet publish (Join-Path $root 'tests/analysis-verifier/AnalysisVerifier.csproj') --configuration Release --runtime $rid --self-contained true --output $output --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "Portable verifier publish failed for $rid." }
    $runtime = Get-Content -LiteralPath (Join-Path $output 'FileWorkbench.AnalysisVerifier.runtimeconfig.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $runtimeVersion = ($runtime.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' }).version
    $noticeTarget = Join-Path $licenses ".NET-$rid"
    New-Item -ItemType Directory -Path $noticeTarget -Force | Out-Null
    foreach ($name in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
        $inputPath = Join-Path $packages "microsoft.netcore.app.runtime.$rid/$runtimeVersion/$name"
        if (-not (Test-Path -LiteralPath $inputPath)) { throw "Runtime notice missing: $inputPath" }
        Copy-Item -LiteralPath $inputPath -Destination $noticeTarget
    }
}
$hashingNotices = Join-Path $licenses 'System.IO.Hashing'
New-Item -ItemType Directory -Path $hashingNotices -Force | Out-Null
$hashingNoticeFiles = @(Get-ChildItem -LiteralPath (Join-Path $packages 'system.io.hashing/10.0.13') -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' })
$supplement = Join-Path $root 'third-party-notices/system.io.hashing-10.0.13'
$hashingSource = Get-Content -LiteralPath (Join-Path $supplement 'SOURCE.json') -Raw -Encoding UTF8 | ConvertFrom-Json
[xml]$hashingSpec = Get-Content -LiteralPath (Join-Path $packages 'system.io.hashing/10.0.13/system.io.hashing.nuspec') -Raw -Encoding UTF8
if ($hashingSpec.package.metadata.id -ne $hashingSource.package -or $hashingSpec.package.metadata.version -ne $hashingSource.version -or
    $hashingSpec.package.metadata.repository.commit -ne $hashingSource.commit -or
    (Get-FileHash -LiteralPath (Join-Path $supplement $hashingSource.file) -Algorithm SHA256).Hash -ne $hashingSource.sha256) {
    throw 'Pinned System.IO.Hashing notice provenance/checksum mismatch.'
}
$hashingNoticeFiles += Get-Item -LiteralPath (Join-Path $supplement $hashingSource.file)
$hashingNoticeFiles | Copy-Item -Destination $hashingNotices
Copy-Item -LiteralPath (Join-Path $supplement 'SOURCE.json') -Destination $hashingNotices
$metadata = [ordered]@{
    formatVersion = 1; kind = 'private-analysis-platform-verification'; sourceCommit = ((& git -C $root rev-parse HEAD) -join '').Trim()
    sourceWorktreeDirty = [bool]((& git -C $root -c core.excludesFile=.gitignore status --porcelain) -join '')
    targets = @('win-x64', 'linux-arm64', 'osx-arm64'); nativeSourceSha256 = $expected.ToLowerInvariant()
    nativeBuildRequiredOnTarget = $true; guiIncluded = $false; platformAcceptancePending = $true
}
[IO.File]::WriteAllText((Join-Path $kit 'analysis-platform-kit.json'), ($metadata | ConvertTo-Json) + "`n", [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $root 'docs/analysis-platform-kit.md') -Destination (Join-Path $kit 'README.md')
$lines = foreach ($file in (Get-ChildItem -LiteralPath $kit -File -Recurse | Sort-Object FullName)) {
    (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $file.FullName.Substring($kit.Length + 1).Replace('\', '/')
}
[IO.File]::WriteAllLines((Join-Path $kit 'CHECKSUMS.sha256'), $lines, [Text.UTF8Encoding]::new($false))
$archive = "$kit.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($kit, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$archive.sha256", "$archiveHash  $([IO.Path]::GetFileName($archive))`n", [Text.UTF8Encoding]::new($false))
Write-Host "Analysis platform kit: $kit"
Write-Host "Transfer archive: $archive"
