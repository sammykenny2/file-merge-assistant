# Private verification payloads — 2026-10-24

These are retained local inspection artifacts, not a published release or a
clean-machine acceptance result. The owner will run Windows Sandbox/VM and
Pi/macOS validation; no installation on the development host was changed.

## Current payloads after completion-audit fixes

Both kits were rebuilt from clean commit `87afbd8`. The Windows kit includes
full Hub list virtualization and consistent source-root services; the platform
kit includes optional two-root analysis verification, without a GUI.
This evidence documentation is a later commit; payload provenance
continues to identify the code that was actually built.

Windows kit: `build/windows-validation-5747d1b5bf2a40d6a0b72c94dd729b21`.
All 18 inner hashes passed, and its native source archive matches the portable
package's SOURCE.json reference. A fresh extraction passed the real package
checks under Windows PowerShell 5.1 with system-only child PATH: exact answers
for both engines, PNG resize evidence, unchanged source bytes, native descriptor/
imports and the missing-DLL diagnostic. Evidence:
`build/final-windows-probe-7570b5260362428d8e335547cf0c8c46/reports/package-62b8cc3e02ae41d0a3c0bdd03e711b1b/report.json`.
This is sanitized-host execution, not clean-VM lifecycle acceptance.

| Current Windows artifact | SHA-256 |
|--------------------------|---------|
| `FileMergeAssistant-0.2.1-x64-debug-setup.exe` | `e850920bbfb4593d2bc1f496408dab8b9b43342e57e932a2775a0ae11c80b710` |
| `FileMergeAssistant-0.2.1-x64-debug-portable.zip` | `b61305087f3b9df47002306a802f1b1c94503aefc8abb3b17ac93b002490b40e` |
| `FileMergeAssistant-0.2.1-czkawka-0.2.0-debug-source.zip` | `a0c370346d0104ca4dd509eae06274c0be07ec932f7e79e4fb949dd24449574b` |

Platform archive:
`build/analysis-platform-7e73ecafd9664cd4bf985b9f4a2eb6aa.zip` (189,289,454 bytes).
SHA-256: `c34433715401272e5ca3e433ceffe9949acf3563fbedc99232d2c4015d5f4a54`.
The outer hash and all 26,442 inner hashes passed. PE x64, ELF64 AArch64 and
Mach-O64 arm64 apphost headers match their target directories. Exported
VerificationRoots/VolumeProof sources, tests, Program and second-volume script
match the reviewed checkout. Metadata records clean
`87afbd86ace32b4324be6a5c36b12de16e4fb685` and the matching native source hash.
Headers and cross-publishing do not verify foreign startup/native linking.

The Windows self-contained verifier ran with system-only PATH and fresh roots
using the packaged native DLL. Required checks passed, including the three-
algorithm cross-root request. Both roots had the same volume GUID, so
`distinct-volume-known-answer` was explicitly Skipped and
`crossVolumeVerified` stayed false. Symlink creation was Skipped for missing
privilege; Unix permissions were NotApplicable. Evidence:
`build/platform-final-probe-581f6edd87a94a8ea13d6572b7d2ad14/first/report.json`
and its parent `kit-evidence.json`.

Use the new Windows kit README for disposable lifecycle and visible GUI checks.
For Pi/macOS, follow [the target instructions](../analysis-platform-kit.md),
optionally providing `--second-work-parent` on another mounted volume. Return
the complete printed report directory and retain both generated fixture roots.
Target native build/run, actual different-volume evidence, physical USB/HDD,
controlled-cache/visible input and VM install/upgrade/uninstall remain owner-run
gates. No installed host application, user data, CI settings or engine defaults
were changed by this rebuild.

## Earlier Windows x64 payload (8fa59bb)

This earlier build uses commit `8fa59bb`, including the portable analysis assembly and
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

## Earlier Pi arm64 and Apple Silicon payload

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
