# Setup Guide

File Workbench currently runs on Windows x64 with a C#/.NET 10 WPF GUI, CLI and SQLite
index. The optional analysis DLL is a pinned Rust wrapper around Czkawka core.
Python and Node.js are not application prerequisites.

The product is **File Workbench**, or **貓檔家** in Traditional Chinese, with
the subtitle **整理檔案的專家**. Assemblies use `FileWorkbench.*`; the packaged
CLI command is `file-workbench`. Fresh profiles use `%LOCALAPPDATA%\FileWorkbench`;
existing `%LOCALAPPDATA%\FileMergeAssistant` data is reused without moving files.
`FWB_DATA_DIR` takes priority over the legacy `FMA_DATA_DIR` alias.

## Run a local package

Preview binaries are listed in [GitHub Releases](https://github.com/sammykenny2/messy-file-workbench/releases);
local build artifacts are also experimental.
The NSIS installer installs for the current user. The portable ZIP contains
`app/` and `cli/`; keep them together so both entry points use the same DLL.
End users need neither a .NET SDK/runtime nor a Rust/MSVC toolchain.

For an isolated GUI preview, extract the ZIP into a new folder and run:

```powershell
$env:FWB_DATA_DIR = Join-Path $env:TEMP ('file-workbench-preview-' + [guid]::NewGuid().ToString('N'))
.\app\FileWorkbench.exe
```

Use generated/sample folders initially. `--demo` shows simulated workbench data.
Closing the window may keep File Workbench in the tray; use its Exit command to stop it.
Do not install over a running instance. Data and installation ownership are
explained in [deployment](deployment.md).

## Build on Windows x64

Install the .NET 10 SDK selected by `global.json`. To build the optional native
adapter, also install rustup, Rust 1.99.1 and Visual Studio C++ x64 build tools
with a Windows SDK. The Cargo lockfile pins the upstream commit; no sibling fork
checkout is used. The local validation baseline uses .NET SDK 10.0.402.

```powershell
dotnet restore messy-file-workbench.sln
dotnet build messy-file-workbench.sln --configuration Debug
.\scripts\windows\Build-NativeAnalysis.ps1 -Test
```

The last command runs Rust fmt/tests/clippy and the complete Debug .NET solution
against the real native DLL and isolated test DLLs. Without native fixtures,
several integration tests are skipped; that does not establish native acceptance.
Avoid running publish and solution tests simultaneously: they share restore/build
outputs. See [testing](testing.md) for individual commands and recorded results.

To build NSIS, portable binaries and the native rebuild source:

```powershell
.\scripts\windows\Build-Installer.ps1 -Configuration Debug
.\scripts\windows\New-AnalysisValidationKit.ps1
```

`build/` contains ignored artifacts. Each installer run retains a unique working
folder for inspection. `-WithoutNative` explicitly builds a managed-only package.
Native binary output uses Release/static CRT even when the .NET application is
Debug; measurements must distinguish those configurations.

The 0.3.0 Preview omits ImageSharp/CoenM and needs no Six Labors key, including
Release builds. Legacy C# `perceptual`/`video-perceptual` algorithms are disabled;
native PNG/JPEG analysis and review remain available. See
[the pinned notice policy](../native/file-workbench-czkawka-ffi/third-party-notices/README.md)
for original notices and the two explicitly labeled standard MIT supplements.

## Development entry points

```powershell
dotnet run --project src/Gui --configuration Debug
dotnet run --project src/Cli --configuration Debug -- engines
dotnet run --project src/Cli --configuration Debug -- analyze C:\Samples --engine csharp
```

A source build does not automatically copy the native DLL into GUI/CLI output.
A normal application looks only under its executable-relative
`runtimes/<rid>/native` directory. A marked packaged `cli/` can share its marked
sibling `app/` DLL. It never searches scanned folders, the current directory or
PATH. Test-specific environment variables do not alter production resolution.
Use the package builder for a complete GUI/CLI payload. Never stage DLLs from
`native/test-libraries` or the test-only scan host.

## Experimental analysis workflow

Add multiple folders to the workbench's persistent queue. Settings selects the
engine/algorithm for new exact-content jobs; existing jobs retain their frozen
engine/version/scope. The default remains managed XxHash128. Both engines remain
Experimental; missing or incompatible native modules produce explicit diagnostics.

The separate Image review page handles PNG/JPEG candidates, parallel previews
and saved manual notes. Existing reviews can reopen without the optional engine.
Similarity does not create exact-content operation plans. See
[image review](image-review.md) and [analysis CLI](analysis-cli.md) for limits.

Windows clean install/update/uninstall checks belong in the owner's Sandbox/VM,
using the generated validation kit. Pi arm64/Wayland and Apple Silicon macOS
validation will be run by the owner separately; WPF is not a cross-platform GUI.
CI automatically runs actual Windows and arm64 portable tests on pushes/PRs.
Preview publication remains manual and requires successful CI for the same commit.

## Portable analysis verification

`src/analysis/Analysis.csproj` targets `net10.0` independently of Windows Core,
WPF and ImageSharp. Its [standalone tests](testing.md#portable-analysis-leaf)
can run with a .NET 10 SDK. This is a read-only engine surface, not a portable
File Workbench application. Native compilation is still required for each target.

To prepare the owner's private self-contained Windows x64/Linux arm64/macOS
arm64 verifier and vendored source archive on Windows:

```powershell
.\scripts\windows\New-AnalysisPlatformKit.ps1
```

First build the native inspection source archive as described above. The new
kit has its own ZIP/checksum, source, dependency notices and retained build
directory. Follow [the target instructions](analysis-platform-kit.md) on the Pi
or Mac, then review its returned report before claiming any platform support.
