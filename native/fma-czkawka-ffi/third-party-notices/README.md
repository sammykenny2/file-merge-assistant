# Pinned dependency notices

`sources.json` records verbatim license/notice files missing from the published
crate archives. Each URL uses the complete upstream commit recorded in that
crate's `.cargo_vcs_info.json`; SHA-256 validates the copied bytes. Packaging
copies these files and their provenance beside the corresponding dependency.
The wrapper and Czkawka core licenses do not replace dependency licenses.

The current lockfile also resolves `realfft 3.5.0`, `rusty-chromaprint 0.3.0` and
`simd_helpers 0.1.0`, whose crate manifests declare MIT but contain no standalone
license text; none was found in their recorded upstream commits either:

- [realfft d0d4eee](https://github.com/HEnquist/realfft/tree/d0d4eee0525fd27c96c8a046d6d107acd5ed84a6)
- [rusty-chromaprint 97d67df](https://github.com/darksv/rusty-chromaprint/tree/97d67dfed02854ac5d5eaa9ac965b9abe3dff690)
- [simd_helpers ca1a2f8](https://github.com/lu-zero/simd_helpers/tree/ca1a2f84aa386d758e98f8a609d990263932fb85)

These are unresolved notice collection items, not a claim that those crates use a
different license or that every resolved crate is linked into the final DLL.
`Export-NativeAnalysis.ps1` fails closed by default. Only internal Debug
application packaging passes `-AllowIncompleteNotices`; its manifest retains
`noticeCollectionComplete=false`, the package/version list and actual upstream
Cargo declarations. Resolve these items before a public Release package, along
with the separate ImageSharp key requirement. No missing copyright years or
upstream license documents have been fabricated.
