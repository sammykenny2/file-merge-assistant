# Private analysis platform verification kit

This kit is for the owner's Raspberry Pi arm64 (labwc/Wayland) and Apple Silicon
macOS. The exact OS/build/filesystem will be recorded on the target; the supplied
macOS nickname is not used to infer a version. No remote connection is needed.

It contains a self-contained .NET 10 verifier, its portable source/tests and the
pinned Rust wrapper/dependencies. It does not contain a macOS/Linux FMA GUI,
persistent workbench or file-operation implementation. WPF remains Windows-only.
No platform support is declared until the returned evidence is reviewed.

The native source inventory originally came from Windows; see
`DEPENDENCIES.windows.json`. It includes three unresolved notice-collection
items (realfft, rusty-chromaprint, simd_helpers). Source and declarations are
preserved, but this private inspection kit is not cleared for public
redistribution. The target script records its actual Cargo dependency graph.
ImageSharp is absent from this verifier; the separate full FMA Release build
still needs the owner's missing ImageSharp key.

## Prerequisites

- 64-bit Linux arm64 with glibc, or Apple Silicon macOS, using a regular user.
- A C compiler/native build tools and rustup with Rust 1.99.1 installed. On macOS
  this means working Apple command-line build tools. The script does not install
  or change system tools. Install the pinned toolchain before running:

  ```bash
  rustup toolchain install 1.99.1 --profile minimal
  ```

- Several GB of free space for native compilation and retained logs. Rust builds
  one compiler job at a time to limit memory pressure on a Pi.
- The bundled self-contained verifier does not need a .NET SDK. Linux still needs
  the OS runtime libraries required by .NET. Use Microsoft's current
  [Linux](https://learn.microsoft.com/en-us/dotnet/core/install/linux) or
  [macOS](https://learn.microsoft.com/en-us/dotnet/core/install/macos) instructions
  for OS prerequisites; [rustup installation](https://rust-lang.github.io/rustup/installation/index.html)
  documents Rust setup. If startup/security checks block execution, retain the
  error; the script does not remove quarantine or other security settings.

## Run on the target

Extract the complete kit into a new directory with a short path. Keep its source,
vendor, tests and bin folders together. From that directory:

The builder writes a unique `analysis-platform-<id>.zip` and adjacent `.sha256`.
Verify that outer hash before extraction; the target runner also verifies every
listed inner file before compiling or executing payloads.

```bash
bash scripts/testing/verify-analysis-platform.sh
```

For a particular test filesystem, supply an existing parent directory. The
script creates a new `fma-platform.XXXXXX` folder there and never scans the
parent's existing files:

```bash
bash scripts/testing/verify-analysis-platform.sh --work-parent /path/to/test-volume
```

It checks all shipped file hashes, records OS/architecture/filesystem details,
builds/tests the native adapter from vendored source with `--locked --offline`,
and runs the architecture-matched verifier. It does not install, run a GUI,
create operation plans, or delete reports. Compilation and the Rust tests can
take time. The output prints the retained report directory; return that complete
directory and any terminal errors. Neither passwords nor SSH details are needed.

The verifier checks known exact answers for managed XxHash128/SHA-256 and native
BLAKE3, Unicode/case/normalization behavior, PNG resize evidence, symlink refusal,
Unix permission denial and unchanged known-answer source bytes. `report.json`
records each case's Passed/Failed/Skipped/NotApplicable status. A root/privileged
user that can bypass mode 000 fails the permission case; rerun as a regular user.
Skipped checks are not support evidence.

## Limits and interpretation

- Results cover direct read-only analysis of supplied Unicode paths, not complete
  FMA support. No WPF, SQLite migration, startup, recycling or restore is tested.
- Non-Windows observations currently use size/time without a physical identity
  provider. Same-size/same-time replacement protection therefore remains a
  platform gap. No arbitrary-byte POSIX filename enumeration is claimed.
- A test on one filesystem does not verify cross-volume behavior. Run on each
  desired filesystem and report both volumes separately; operations across
  volumes remain unsupported here.
- Wayland/labwc is recorded as the Pi's environment, not tested as a GUI backend.
- Native tests have platform-dependent counts: the static-CRT test applies only
  to Windows/MSVC. Do not copy a Windows test total onto another platform.

If the .NET 10 SDK is installed, the included pure analysis contract tests can
also run separately after the native library has been built:

```bash
export FMA_NATIVE_LIBRARY_DIR=/absolute/path/to/the/report/native
dotnet test tests/analysis-tests/Analysis.Tests.csproj --configuration Release --verbosity minimal
```

Native tests skipped because the variable is absent do not establish acceptance.
Do not use `dotnet test` on an unrelated checkout or personal-data directory.
