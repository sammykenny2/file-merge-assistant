<#
.SYNOPSIS
    Verify a staged or installed Windows x64 analysis package without changing its installation.
.DESCRIPTION
    Uses the real published CLI with a system-only PATH, no SDK/toolchain paths,
    isolated FMA data and bundle extraction directories, and generated known-answer
    files. Checks the native manifest, PE imports, both exact engines, image
    candidates and a missing-library diagnostic. This is a sanitized-host test;
    it is not evidence of a clean VM unless actually run there. Never installs.
.PARAMETER PackageRoot
    Directory containing the package-owned app and cli folders.
.PARAMETER FixturesRoot
    Fixtures directory containing analysis/known-answer.json and similar-images/.
.PARAMETER ReportParent
    Parent for a new uniquely named report directory. No existing files are removed.
.EXAMPLE
    .\scripts\windows\Test-AnalysisPackage.ps1 -PackageRoot C:\FmaKit\portable -FixturesRoot C:\FmaKit\Fixtures -ReportParent C:\FmaReports
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageRoot,
    [Parameter(Mandatory)][string]$FixturesRoot,
    [Parameter(Mandatory)][string]$ReportParent
)

$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') { throw 'Run Windows x64 PowerShell.' }
$PackageRoot = [IO.Path]::GetFullPath($PackageRoot)
$FixturesRoot = [IO.Path]::GetFullPath($FixturesRoot)
$run = Join-Path ([IO.Path]::GetFullPath($ReportParent)) ('package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$checks = [Collections.Generic.List[string]]::new()
$commands = [Collections.Generic.List[object]]::new()
$report = [ordered]@{
    formatVersion = 1; kind = 'sanitized-host-package'; startedUtc = [DateTime]::UtcNow.ToString('o')
    platform = [Environment]::OSVersion.VersionString; processArchitecture = 'x64'; packageRoot = $PackageRoot
    noSdkOrRustOnChildPath = $true; passed = $false; checks = @(); commands = @(); error = $null
}

function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }

function Invoke-PackageCli([string]$Executable, [string[]]$Arguments, [string]$Name, [int]$ExpectedExit = 0) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $Executable
    # Windows filenames cannot contain quotes. Double terminal backslashes inside a quoted argument.
    $start.Arguments = (($Arguments | ForEach-Object {
        if ($_ -match '"') { throw 'Unexpected quote in CLI argument.' }
        '"' + ($_ -replace '(\\+)$', '$1$1') + '"'
    }) -join ' ')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $start.WorkingDirectory = $run
    $start.EnvironmentVariables['PATH'] = Join-Path $env:SystemRoot 'System32'
    foreach ($environmentName in @('DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_ROOT(x86)', 'FMA_NATIVE_LIBRARY_DIR', 'FMA_WRONG_ABI_LIBRARY_DIR', 'FMA_CANCEL_UPLOAD_LIBRARY_DIR')) {
        $start.EnvironmentVariables.Remove($environmentName)
    }
    $start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $start.EnvironmentVariables['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $run 'bundles'
    $start.EnvironmentVariables['FMA_DATA_DIR'] = Join-Path $run ('data-' + $Name)
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    try {
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $null = $process.Start()
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(180000)) { $process.Kill(); $process.WaitForExit(); throw "CLI timeout: $Name" }
        $output = $outputTask.GetAwaiter().GetResult()
        $errorOutput = $errorTask.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $run "$Name.json"), $output, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $run "$Name.stderr.txt"), $errorOutput, [Text.UTF8Encoding]::new($false))
        $commands.Add([ordered]@{ name = $Name; exitCode = $process.ExitCode; elapsedMilliseconds = $clock.ElapsedMilliseconds })
        Require ($process.ExitCode -eq $ExpectedExit) "CLI $Name exited $($process.ExitCode), expected $ExpectedExit; inspect $run."
        return ($output | ConvertFrom-Json)
    } finally { $process.Dispose() }
}

try {
    $app = Join-Path $PackageRoot 'app'
    $cli = Join-Path $PackageRoot 'cli/fma.exe'
    foreach ($folder in @($app, (Split-Path -Parent $cli))) {
        Require (Test-Path -LiteralPath (Join-Path $folder '.installed-by-file-merge-assistant-setup') -PathType Leaf) "Missing package ownership marker: $folder"
    }
    Require (Test-Path -LiteralPath $cli -PathType Leaf) 'Missing published CLI.'
    Require (Test-Path -LiteralPath (Join-Path $app 'FileMergeAssistant.exe') -PathType Leaf) 'Missing GUI.'
    $runtime = Get-Content -LiteralPath (Join-Path $app 'FileMergeAssistant.runtimeconfig.json') -Encoding UTF8 -Raw | ConvertFrom-Json
    Require (@($runtime.runtimeOptions.includedFrameworks).Count -eq 2) 'GUI must include both .NET and Windows Desktop runtimes.'
    $unexpected = @(Get-ChildItem -LiteralPath $PackageRoot -Recurse -File | Where-Object { $_.Name -match '(wrong-abi|scan-host|upload-cancel|\.Tests\.)' })
    Require ($unexpected.Count -eq 0) 'Test-only artifacts must not ship.'
    $native = Join-Path $app 'runtimes/win-x64/native'
    $manifest = Get-Content -LiteralPath (Join-Path $native 'native-manifest.json') -Encoding UTF8 -Raw | ConvertFrom-Json
    $dll = Join-Path $native 'fma_czkawka_ffi.dll'
    Require ($manifest.formatVersion -eq 1 -and $manifest.target -eq 'x86_64-pc-windows-msvc' -and $manifest.staticCrt) 'Unexpected native package manifest.'
    Require ((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant() -eq $manifest.sha256) 'Native DLL checksum mismatch.'
    Require (Test-Path -LiteralPath (Join-Path $app 'Licenses/Rust/DEPENDENCIES.json') -PathType Leaf) 'Native dependency notices are missing.'
    Require ($null -ne $manifest.noticeCollectionComplete) 'Native manifest must report notice-collection completeness.'
    $declaredOnly = @()
    foreach ($package in $manifest.packages) {
        $noticeFolder = [IO.Path]::GetFullPath((Join-Path $app ('Licenses/Rust/' + $package.noticeDirectory)))
        $licenseRoot = [IO.Path]::GetFullPath((Join-Path $app 'Licenses/Rust'))
        Require ($noticeFolder.StartsWith($licenseRoot + '\', [StringComparison]::OrdinalIgnoreCase)) 'Invalid dependency notice directory.'
        foreach ($notice in $package.noticeFiles) {
            $noticePath = [IO.Path]::GetFullPath((Join-Path $noticeFolder $notice))
            Require ($noticePath.StartsWith($noticeFolder + '\', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $noticePath -PathType Leaf)) "Missing declared dependency notice: $($package.name) / $notice"
        }
        if ($package.noticeStatus -eq 'DeclaredOnly') {
            $declaredOnly += "$($package.name) $($package.version)"
            Require (Test-Path -LiteralPath (Join-Path $noticeFolder 'MISSING-NOTICE.txt')) 'Missing notice diagnostic was not packaged.'
        } else { Require ($package.noticeStatus -eq 'Collected' -and @($package.noticeFiles).Count -gt 0) 'Unexpected dependency notice status.' }
    }
    Require (($declaredOnly.Count -eq 0) -eq [bool]$manifest.noticeCollectionComplete) 'Notice completeness flag contradicts dependency records.'
    Require ((($declaredOnly | Sort-Object) -join '|') -eq (($manifest.missingNotices | Sort-Object) -join '|')) 'Missing notice summary contradicts dependency records.'
    $sourceReference = Get-Content -LiteralPath (Join-Path $app 'Licenses/Rust/SOURCE.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Require ($sourceReference.formatVersion -eq 1 -and $sourceReference.sha256 -match '^[0-9a-f]{64}$' -and
        $sourceReference.archive -match '^FileMergeAssistant-[0-9.]+-czkawka-[0-9.]+(?:-debug)?-source\.zip$') 'Invalid native rebuild source reference.'

    # Minimal bounded PE32+ reader. No SDK, dumpbin or external module is required on the validation VM.
    if (-not ('FmaPackagePe' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Collections.Generic;
public static class FmaPackagePe {
  public static string[] Imports(string path) {
    using (var stream = File.OpenRead(path)) using (var r = new BinaryReader(stream)) {
      if (r.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Missing MZ");
      stream.Position = 0x3c; int pe = r.ReadInt32();
      if (pe < 64 || pe > stream.Length - 24) throw new InvalidDataException("PE offset");
      stream.Position = pe;
      if (r.ReadUInt32() != 0x4550 || r.ReadUInt16() != 0x8664) throw new InvalidDataException("Not Windows x64 PE");
      int sections = r.ReadUInt16(); stream.Position = pe + 20; int optionalSize = r.ReadUInt16();
      long optional = pe + 24;
      stream.Position = optional;
      if (r.ReadUInt16() != 0x20b || optionalSize < 224 || sections < 1 || sections > 96) throw new InvalidDataException("PE32+ header");
      stream.Position = optional + 120; uint importRva = r.ReadUInt32();
      stream.Position = optional + 216;
      if (r.ReadUInt32() != 0) throw new InvalidDataException("Unexpected delay imports");
      var map = new List<uint[]>(); stream.Position = optional + optionalSize;
      for (int i=0; i<sections; i++) {
        stream.Position += 8; uint size = r.ReadUInt32(), address = r.ReadUInt32(), rawSize = r.ReadUInt32(), raw = r.ReadUInt32();
        map.Add(new uint[]{ address, Math.Max(size, rawSize), raw }); stream.Position += 16;
      }
      Func<uint,long> offset = v => {
        foreach (var s in map) if (v >= s[0] && (ulong)v < (ulong)s[0]+s[1]) {
          long p = s[2] + (long)v - s[0]; if (p >= 0 && p < stream.Length) return p;
        }
        throw new InvalidDataException("PE RVA out of range");
      };
      var imports = new List<string>(); long descriptor = offset(importRva);
      for (int i=0; i<256; i++) {
        stream.Position = descriptor + i*20; uint first = r.ReadUInt32(); stream.Position += 8; uint name = r.ReadUInt32(), thunk = r.ReadUInt32();
        if (first == 0 && name == 0 && thunk == 0) return imports.ToArray();
        stream.Position = offset(name); var bytes = new List<byte>();
        for (int j=0; j<256; j++) { byte b = r.ReadByte(); if (b==0) break; bytes.Add(b); if (j==255) throw new InvalidDataException("Import name too long"); }
        imports.Add(System.Text.Encoding.ASCII.GetString(bytes.ToArray()));
      }
      throw new InvalidDataException("Too many imports");
    }
  }
}
'@
    }
    $imports = [FmaPackagePe]::Imports($dll)
    foreach ($import in $imports) {
        Require ($import -match '^(kernel32|ntdll|bcryptprimitives|api-ms-win-core-synch-l1-2-0)\.dll$') "Unexpected native runtime dependency: $import"
    }
    $report.native = [ordered]@{ manifest = $manifest; imports = @($imports) }
    $checks.Add('x64 DLL hash and system-only imports; self-contained GUI; package markers and no test artifacts')

    $fixture = Get-Content -LiteralPath (Join-Path $FixturesRoot 'analysis/known-answer.json') -Encoding UTF8 -Raw | ConvertFrom-Json
    $dataset = Join-Path $run 'sources'
    $paths = @{}
    foreach ($file in $fixture.files) {
        $path = Join-Path $dataset $file.path
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        [IO.File]::WriteAllText($path, $file.content, [Text.UTF8Encoding]::new($false))
        $paths[[string]$file.id] = $path
    }
    $before = @{}
    foreach ($path in $paths.Values) { $before[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    $engines = Invoke-PackageCli $cli @('engines') 'engines'
    Require ($engines.formatVersion -eq 1) 'Unexpected engine report format.'
    $nativeEngine = $engines.engines | Where-Object { $_.id -eq 'czkawka' }
    Require ($nativeEngine.version -eq $manifest.engineVersion -and $nativeEngine.abiVersion -eq $manifest.abiVersion -and
        $nativeEngine.upstreamCommit -eq $manifest.upstreamCommit) 'Loaded engine does not match the packaged native manifest.'
    foreach ($engineId in @('csharp', 'czkawka')) {
        $engine = @($engines.engines | Where-Object { $_.id -eq $engineId })
        Require ($engine.Count -eq 1 -and $engine[0].availability -eq 'Available') "Packaged engine unavailable: $engineId"
        $analysis = Invoke-PackageCli $cli @('analyze', (Join-Path $dataset 'primary'), (Join-Path $dataset 'backup'), '--engine', $engineId) $engineId
        Require ($analysis.isComplete -and $analysis.files.Count -eq $fixture.files.Count) "Incomplete packaged analysis: $engineId"
        $actualGroups = @($analysis.exactGroups | ForEach-Object {
            $ids = $_.fileIds
            (@($analysis.files | Where-Object { $_.id -in $ids } | ForEach-Object { $_.path } | Sort-Object) -join '|')
        } | Sort-Object)
        $expectedGroups = @($fixture.exactGroups | ForEach-Object { (@($_ | ForEach-Object { $paths[[string]$_] } | Sort-Object) -join '|') } | Sort-Object)
        Require (($actualGroups -join "`n") -ceq ($expectedGroups -join "`n")) "Known-answer groups differ: $engineId"
        Require (@($analysis.jobs).Count -eq 2) 'Both sources must be independently inventoried.'
    }
    $checks.Add('both production engines: all seven known-answer files and both cross-source groups')

    $images = Join-Path $run 'images'
    New-Item -ItemType Directory -Path $images | Out-Null
    foreach ($name in @('a-original.png', 'a-resize.png')) { Copy-Item -LiteralPath (Join-Path $FixturesRoot "similar-images/$name") -Destination $images }
    $imageResult = Invoke-PackageCli $cli @('similar-images', $images, '--distance', '6') 'images'
    Require ($imageResult.isComplete -and @($imageResult.pairs).Count -eq 1 -and $imageResult.pairs[0].distance -eq 0 -and @($imageResult.analysis.exactGroups).Count -eq 0) 'Packaged image candidate result differs.'
    foreach ($path in $paths.Values) { Require ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $before[$path]) 'Analysis changed a source file.' }
    foreach ($name in @('a-original.png', 'a-resize.png')) {
        Require ((Get-FileHash -LiteralPath (Join-Path $images $name)).Hash -eq (Get-FileHash -LiteralPath (Join-Path $FixturesRoot "similar-images/$name")).Hash) 'Image analysis changed a source file.'
    }
    $checks.Add('PNG resize candidate with distance zero; exact and image sources unchanged')

    $missing = Join-Path $run 'missing/cli'
    New-Item -ItemType Directory -Path $missing | Out-Null
    Copy-Item -LiteralPath $cli -Destination $missing
    $missingCli = Join-Path $missing 'fma.exe'
    $missingEngines = Invoke-PackageCli $missingCli @('engines') 'missing-engines'
    Require (($missingEngines.engines | Where-Object { $_.id -eq 'czkawka' }).availability -eq 'MissingDependency') 'Missing native DLL must remain visible.'
    $failure = Invoke-PackageCli $missingCli @('analyze', $dataset, '--engine', 'czkawka') 'missing-analysis' 2
    Require (-not $failure.isComplete -and $failure.outcome -eq 'MissingDependency') 'Missing DLL must fail explicitly without fallback.'
    $checks.Add('missing DLL: diagnostic and exit 2, no hidden managed fallback')
    $report.passed = $true
} catch {
    $report.error = $_.Exception.Message
    throw
} finally {
    $report.checks = @($checks)
    $report.commands = @($commands)
    $report.finishedUtc = [DateTime]::UtcNow.ToString('o')
    [IO.File]::WriteAllText((Join-Path $run 'report.json'), ($report | ConvertTo-Json -Depth 16) + "`n", [Text.UTF8Encoding]::new($false))
    Write-Host "Package report: $(Join-Path $run 'report.json')"
}
