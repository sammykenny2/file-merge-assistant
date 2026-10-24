# Private verification payloads — 2026-10-24

These are retained local inspection artifacts, not a published release or a
clean-machine acceptance result. The owner will run Windows Sandbox/VM and
Pi/macOS validation; no installation on the development host was changed.

## Windows x64

Final build uses commit `8fa59bb`, including the portable analysis assembly and
virtualized conflict/outcome controls. .NET is Debug; the native adapter is
Release/static CRT, adapter 0.2.0 / ABI 1 / pinned Czkawka commit
`eb8b91dbb4d25dff416202674ee0de84ee4b5e5c`.

Kit: `build/windows-validation-4181a858fff243dba255be13219f58ab`.
All 18 kit checksums passed. A new extraction tested with Windows PowerShell
5.1 passed both real engines' seven-file/two-group answers, PNG resize evidence,
unchanged source bytes, exact native descriptor/hash/system imports and the
missing-DLL diagnostic. Child PATH contained no SDK or Rust tools; this is not
the same as execution on a clean machine.

Evidence: `build/final-windows-probe-73f13223f91244b58c1859d563f70d81/reports/package-9811eca761ce40de8e88fb1894462872/report.json`.
The source archive matches the portable package's SOURCE.json, and its wrapper,
Cargo.lock and toolchain match the tested source. The managed hashing license
and its provenance/checksum were also checked in the staged application.

| Artifact | SHA-256 |
|----------|---------|
| `FileMergeAssistant-0.2.1-x64-debug-setup.exe` | `d7484c940ce96fb3dfb6cce4d5c000f00dcef6cfa2ef589ba9aa33f7357eb2c8` |
| `FileMergeAssistant-0.2.1-x64-debug-portable.zip` | `8ddf9377cdd75aa35bb77302c1306857b5b1a063802224130c5ba65286ce6b78` |
| `FileMergeAssistant-0.2.1-czkawka-0.2.0-debug-source.zip` | `b8f34ece3b5be4ede35ab81cfac1adeb59600552741705d7630bf8f049f1e3f7` |

Use the kit README for disposable install/reinstall/uninstall and GUI checks.
Without an older baseline installer, the lifecycle script tests same-version
payload replacement/reinstall, not a genuine lower-version upgrade. Physical
keyboard/focus cues/high DPI, real input during scanning and USB/HDD remain
owner-run acceptance cases.

## Pi arm64 and Apple Silicon macOS

Archive: `build/analysis-platform-babd6a5f784043e3b69b1931930be121.zip`.
SHA-256: `a6a1be615c6617fa1a694b07b8793d21c78abb438dff2de0adc6f408e6fb3d3e`.
Outer ZIP and all 26,439 inner hashes passed. It includes the corrected
whole-fixture validator, source/tests, vendored native source, runtime notices
and self-contained verifier binaries for win-x64/linux-arm64/osx-arm64.

Its Windows self-contained verifier passed; the symlink-creation case was
explicitly Skipped, Unix permissions NotApplicable. Evidence:
`build/platform-final-probe-1f7c38bcd88c457996435db043086a6d/report.json`.
No Pi/macOS native build or execution is claimed. Follow
[the target instructions](../analysis-platform-kit.md), then return the printed
report tree. The actual OS/filesystem is recorded there.

The kit records source commit `3711a99` with a dirty-worktree marker because its
notice supplement was then uncommitted; that supplement is committed in
`8fa59bb`. Actual payload bytes are bound by the listed inner/outer hashes.
This is not a claim of a clean checkout or of foreign-platform support.

## Open release conditions

ImageSharp's full-application Release key is still missing. Three native
standalone notice texts remain declared-only (realfft, rusty-chromaprint,
simd_helpers); private Debug inspection preserves that diagnostic, while native
export normally stops. The new System.IO.Hashing MIT text is separate and does
not resolve those native gaps. WPF/workflows remain Windows-only; Unix physical
file identity, cross-volume behavior and G5/G6 acceptance remain outstanding.
No push, public release, CI restoration or default-engine promotion was made.
