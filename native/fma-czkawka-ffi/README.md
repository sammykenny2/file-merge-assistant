# FMA Czkawka adapter

This crate owns FMA's read-only integration with the pinned `czkawka_core` commit
in `Cargo.toml`. It does not depend on a sibling checkout. `Cargo.lock` and
`rust-toolchain.toml` are committed; build artifacts are ignored.

Run from this directory so rustup selects the pinned toolchain:

```powershell
cargo test --locked
cargo fmt --all -- --check
cargo clippy --locked --all-targets --no-deps -- -D warnings
```

The first slice proves the upstream public full-content hashing API over the
shared known-answer fixture in `tests/Fixtures/analysis/`. It reads exactly the
requested candidates, including empty and non-duplicate files, without invoking
upstream directory traversal or deletion. The evidence uses `blake3-v1:` and
must never be relabelled as the existing C# `xxh128:` fingerprint.

The version 1 [C ABI](ABI.md) and .NET `NativeAnalysisEngine` now provide
descriptor validation, bounded job upload/polling, per-file evidence, cooperative
cancellation with worker-stop acknowledgement and SafeHandle cleanup. The DLL is
loaded only from an explicit trusted absolute directory (the installed default
is `runtimes/<rid>/native`). The app's engine selection and persisted jobs are
still pending; building this crate does not change the default engine.

On Windows x64, build and run both Rust and real-DLL .NET tests from the repo root:

```powershell
.\scripts\windows\Build-NativeAnalysis.ps1 -Test
```

This also builds the isolated fixtures in `native/test-libraries/wrong-abi`.
Those intentionally incompatible/racing DLLs are test-only and must not ship.
For manual test runs set `FMA_NATIVE_LIBRARY_DIR`, `FMA_WRONG_ABI_LIBRARY_DIR` and
`FMA_CANCEL_UPLOAD_LIBRARY_DIR` to the three directories printed or selected by
the script. Without the first variable, optional native .NET tests are skipped;
that is not native acceptance. The runner uses Debug .NET tests; it does not
waive ImageSharp Release licensing requirements.
