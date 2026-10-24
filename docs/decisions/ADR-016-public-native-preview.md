# ADR-016: Public native-integration preview

## Status

Accepted, 2026-10-24. The owner requested public source, restored CI and a GitHub
Preview, then explicitly asked to omit ImageSharp while evaluating the DLL.

## Context

The source is MIT licensed. ImageSharp-dependent legacy image/video algorithms
prevented a keyless Release build. Native PNG/JPEG evidence and the WPF review
flow do not use ImageSharp. Native notice collection also treated every absent
standalone upstream license file as an unresolved grant, including two crates
whose pinned manifests explicitly declare MIT.

## Decision

- Publish the checked source repository and restore automatic CI. Keep release
  publication manual and versioned, with no replacement of existing tags/assets.
- Ship 0.3.0 as an experimental Windows x64 Preview. Omit ImageSharp/CoenM,
  reject legacy `perceptual` and `video-perceptual` before scanning, and retain
  native BLAKE3 and separate PNG/JPEG candidate review. Do not reinterpret old
  fingerprints, switch defaults or claim G5/G6 acceptance.
- Retain upstream notices and fixed native rebuild sources. Supplement
  simd_helpers from its immediate subsequent official license-only commit.
- For only realfft 3.5.0 and rusty-chromaprint 0.3.0, accept the pinned package's
  MIT declaration with immutable standard SPDX MIT text. Verify the crate
  checksum/VCS commit and preserve the original manifest, README and source.
  Label these `DeclaredSpdxTerms` with
  `upstreamOriginalLicenseFileFound=false`; preserve the template unchanged,
  including its placeholders. Never invent copyright ownership or years.
- `noticeCollectionComplete` means every resolved dependency has collected terms
  under this policy. `upstreamOriginalNoticesComplete` separately records whether
  original upstream files were found. Unknown or contradictory terms still
  fail closed; neither field is an IP ownership or legal-compliance certificate.

## Consequences

Release builds no longer need an ImageSharp key. Legacy C# media hashing is
intentionally unavailable in this preview; old entries remain identifiable and
cannot silently turn into native evidence. Native integration remains testable.

The two standard-term supplements remain visibly distinct from upstream files.
If upstream publishes original notices, replace the supplement with those exact
bytes and provenance. Physical-device, VM and owner-platform acceptance remain
open, and their absence must be stated in the Preview notes.

## Alternatives considered

- Wait for ImageSharp licensing: deferred by the owner's explicit instruction.
- Substitute a different hash under an existing name: rejected because that
  would change persisted algorithm semantics.
- Invent copyright lines or call standard terms an upstream original: rejected.

## References

- [Cargo: license and license-file](https://doc.rust-lang.org/cargo/reference/manifest.html#the-license-and-license-file-fields)
- [SPDX MIT terms](https://spdx.org/licenses/MIT.html)
- [Pinned collection provenance](../../native/fma-czkawka-ffi/third-party-notices/sources.json)
- [Upstream missing-file issue](https://github.com/darksv/rusty-chromaprint/issues/23)
- [PLAN-002](../plans/PLAN-002-pluggable-analysis-engines.md)
