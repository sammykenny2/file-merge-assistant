<#
.SYNOPSIS
    Build the pinned Windows x64 Czkawka analysis adapter.
.DESCRIPTION
    Requires the MSVC Rust build prerequisites and rustup. With -Test, also runs
    Rust checks and Debug .NET solution tests against the real DLL and isolated
    ABI/cancellation fixture DLLs. Fixture DLLs must never be packaged.
.PARAMETER Configuration
    Native build profile. The .NET tests always use Debug; this script does not
    bypass the ImageSharp requirements for Release application builds.
.PARAMETER Test
    Build test-only libraries and run the Rust and .NET regression suites.
.EXAMPLE
    .\scripts\windows\Build-NativeAnalysis.ps1 -Test
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$adapterRoot = Join-Path $projectRoot 'native/fma-czkawka-ffi'
$fixtureRoot = Join-Path $projectRoot 'native/test-libraries/wrong-abi'
$profile = $Configuration.ToLowerInvariant()
$target = 'x86_64-pc-windows-msvc'
$adapterTarget = Join-Path $adapterRoot 'target'
$fixtureTarget = Join-Path $fixtureRoot 'target'
foreach ($flagVariable in @('RUSTFLAGS', 'CARGO_ENCODED_RUSTFLAGS', 'CARGO_BUILD_RUSTFLAGS', 'CARGO_TARGET_X86_64_PC_WINDOWS_MSVC_RUSTFLAGS')) {
    if ([Environment]::GetEnvironmentVariable($flagVariable, 'Process')) {
        throw "Custom $flagVariable would override the pinned static-CRT package settings. Clear it in this process before building."
    }
}
if (-not [Environment]::Is64BitProcess -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
    throw 'This build script currently validates Windows x64 only.'
}

function Invoke-Checked {
    param([string]$Command, [string[]]$Arguments)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}

Push-Location $adapterRoot
try {
    $buildArguments = @('+1.99.1', 'build', '--locked', '--target', $target, '--target-dir', $adapterTarget)
    if ($Configuration -eq 'Release') { $buildArguments += '--release' }
    Invoke-Checked 'cargo' $buildArguments
    if ($Test) {
        Invoke-Checked 'cargo' @('+1.99.1', 'fmt', '--all', '--', '--check')
        Invoke-Checked 'cargo' @('+1.99.1', 'test', '--locked', '--target', $target, '--target-dir', $adapterTarget)
        Invoke-Checked 'cargo' @('+1.99.1', 'clippy', '--locked', '--target', $target, '--target-dir', $adapterTarget, '--all-targets', '--no-deps', '--', '-D', 'warnings')
    }
} finally { Pop-Location }

$nativeDirectory = Join-Path $adapterTarget "$target/$profile"
if (-not (Test-Path -LiteralPath (Join-Path $nativeDirectory 'fma_czkawka_ffi.dll'))) {
    throw 'Cargo did not produce the expected native adapter.'
}
Write-Host "Native adapter: $(Join-Path $nativeDirectory 'fma_czkawka_ffi.dll')"
if (-not $Test) { return }

Push-Location $fixtureRoot
try {
    Invoke-Checked 'cargo' @('+1.99.1', 'build', '--locked', '--target', $target, '--target-dir', $fixtureTarget)
    Invoke-Checked 'cargo' @('+1.99.1', 'build', '--locked', '--target', $target, '--features', 'upload-cancel', '--target-dir', (Join-Path $fixtureTarget 'upload-cancel'))
} finally { Pop-Location }

$testVariables = @{
    FMA_NATIVE_LIBRARY_DIR = $nativeDirectory
    FMA_WRONG_ABI_LIBRARY_DIR = Join-Path $fixtureTarget "$target/debug"
    FMA_CANCEL_UPLOAD_LIBRARY_DIR = Join-Path $fixtureTarget "upload-cancel/$target/debug"
}
$previousValues = @{}
foreach ($name in $testVariables.Keys) {
    if (-not (Test-Path -LiteralPath (Join-Path $testVariables[$name] 'fma_czkawka_ffi.dll'))) {
        throw "Missing native test library: $name"
    }
}
foreach ($name in $testVariables.Keys) {
    $previousValues[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, $testVariables[$name], 'Process')
}
Push-Location $projectRoot
try {
    Invoke-Checked 'dotnet' @('test', 'file-merge-assistant.sln', '--configuration', 'Debug', '--verbosity', 'minimal', '--blame-hang-timeout', '60s', '--blame-hang-dump-type', 'none')
} finally {
    Pop-Location
    foreach ($name in $testVariables.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousValues[$name], 'Process')
    }
}
