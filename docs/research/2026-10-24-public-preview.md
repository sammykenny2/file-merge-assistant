# Public-source and Preview preparation — 2026-10-24

The owner authorized pushing the previous 24 local commits, making the repository
public if checks found no blocker, restoring CI and publishing a versioned
Preview. Those commits were pushed through `810d947`. GitHub visibility was then
changed to public with `gh`; no history was rewritten.

Gitleaks 8.30.1 was downloaded from its official release and checked against its
published archive hash. The all-ref Git scan found no leaks: 90 reachable commits,
72 commits reported by the patch scanner, about 3.03 MB scanned. Independent
inspection found no tracked personal file contents, databases, binaries or keys;
the seven image fixtures are synthetic. Normal Git author names/email remain
part of public commit metadata. This is a scoped scan, not a guarantee that a
scanner detects every possible sensitive value.

The repository had no issues, published releases or Actions artifacts. Nine old
run logs returned HTTP 410. The newest failed validation job had no steps and no
log. No retained log content could be fetched, so an empty-directory scan is not
reported as log coverage. Local redacted evidence is retained under
`build/public-readiness-e2461289d18c4af5815076062e394f5b/`.

The source-publication assessment is separate from binary distribution. The
owner subsequently chose to omit ImageSharp. The notice collection decision and
experimental Preview boundary are in [ADR-016](../decisions/ADR-016-public-native-preview.md).
Full Release regression after removal passed Core 510, GUI 25, CLI 14 and
portable analysis 27, with zero skipped and the real native/test DLLs available.
Native images are still checked by the existing GUI/CLI and contract tests.

Local optimized NSIS/portable/source/manifest packaging completed in
`build/installer/run-f40619382d2e47678af5de17f1af89ce/`. The production CLI
passed both engines, PNG evidence, unchanged-source and missing-DLL checks;
ImageSharp/CoenM/key/test artifacts were absent. Actual PowerShell 5.1 repeated
the package probe successfully at
`build/preview-ps51-99ce4a4022964283859bb778d44d5e3f/package-e134df1684194a048f6ef845bc735c3d/report.json`.
The native manifest records terms collection complete, no missing terms, and
original-file coverage false for the two clearly labeled MIT supplements.
These local artifacts preceded the final commit; the release workflow rebuilds
the reviewed source rather than attributing these bytes to a clean CI run.

CI now defines Windows Rust/full .NET integration and real arm64 Linux/macOS
portable native/.NET jobs. Defining a job is not a passing remote run; actual
results and the published asset hashes must be recorded after execution.
