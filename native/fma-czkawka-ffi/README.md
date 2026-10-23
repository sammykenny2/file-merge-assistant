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

C ABI job exports, the C# native adapter and application integration are still
pending. Building this crate does not enable a Rust engine in FMA.
