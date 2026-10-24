# Pinned dependency notice collection

`sources.json` binds supplements to exact package versions and the VCS commits
recorded by their crate archives. Every supplied file has an immutable URL and
SHA-256. Packaging checks those bytes and preserves original package notices.

Most supplements are original license files from the pinned commit.
`simd_helpers 0.1.0` uses official commit
`82040194cd05affb060bf94d6f19f82a771d07fb`, immediately after its pinned
`ca1a2f84aa386d758e98f8a609d990263932fb85`; the only change adds LICENSE.
The original text is copied unchanged, with its own copyright statement.

Two reviewed cases use `DeclaredSpdxTerms`: realfft 3.5.0 and
rusty-chromaprint 0.3.0. Their pinned crates declare MIT but do not publish a
standalone license file. Cargo defines the license field as the package's SPDX
license declaration. We supply the immutable standard SPDX MIT template,
including its unmodified placeholders, as **standard terms**, not an original
upstream license file. We preserve the original Cargo manifest, README, VCS and
registry checksum evidence and include the full locked source in the rebuild
archive. No author field or guessed year is converted into a copyright notice.

The exporter allows this only for those two names/versions, verifies the MIT
declaration plus pinned VCS/crate checksum, and keeps
`upstreamOriginalLicenseFileFound=false`. `noticeCollectionComplete` records
terms collection under this policy; `upstreamOriginalNoticesComplete` separately
records whether original upstream files were found. Unknown or contradictory
terms continue to block Release. These fields are not legal/IP certification.
If original upstream notices become available, replace the standard supplement
with their verbatim bytes and provenance.

- [Decision](../../../docs/decisions/ADR-016-public-native-preview.md)
- [Cargo license fields](https://doc.rust-lang.org/cargo/reference/manifest.html#the-license-and-license-file-fields)
- [SPDX MIT](https://spdx.org/licenses/MIT.html)
- [realfft author's MIT declaration](https://github.com/HEnquist/realfft/issues/45)
- [rusty-chromaprint missing-file issue](https://github.com/darksv/rusty-chromaprint/issues/23)
