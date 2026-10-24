# Versioned analysis CLI

`engines`, `analyze` and `similar-images` use the same registry, engine snapshots, source policies
and result validator as the WPF workbench. Exact-content and experimental image
analysis are available. The initial managed algorithm is `xxh128-v1`; native Czkawka uses
`blake3-v1`. Both remain Experimental.

```powershell
dotnet run --project src/Cli --no-build -- engines
dotnet run --project src/Cli --no-build -- analyze C:\Samples D:\Backup --engine csharp --algorithm sha256-v1
dotnet run --project src/Cli --no-build -- analyze C:\Samples --engine czkawka
```

The native command requires the matching optional library in the installed
application's `runtimes/<rid>/native` directory. Use `engines` to inspect actual
availability and the supported versioned algorithms. If `--algorithm` is omitted,
`analyze` selects that engine's first advertised exact-content algorithm. A missing
dependency, unknown engine or unsupported algorithm returns a diagnostic; it
does not switch engines or report a successful empty result.

The existing `scan`, `resolve`, `report` and `preview` interfaces retain their
own contracts. In particular, `scan` keeps SHA-256, content matching, recursion,
its existing report formats and the existing operation defaults. The new
`analyze` JSON is a separate versioned format, not a replacement for old reports.

## Scope and results

Each `analyze` invocation creates its own durable `analysis-<id>.db` under the
FMA data directory (`FMA_DATA_DIR` can isolate it). It does not enqueue work into
the normal GUI index or create an operation plan. Its JSON contains the index
path for inspection, per-source frozen job snapshots, generations, full file
inventory, confirmed fingerprints, exact groups and per-file/terminal states.
Files with unique sizes remain in inventory as `NotContentCandidate` after a
complete run. Empty files are eligible for exact-content evidence.

Scope follows the workbench's default exclusions, regenerable-folder, cloud and
link policies. Sources must be distinct and non-overlapping. The FMA data folder
is excluded by its physical location, including when configured through an
alias. Sources inside that folder are rejected. Analysis reads source files;
the index/cache writes are confined to the controlled data folder.

Results on standard output use `formatVersion: 1`. Redirect the JSON to a report
file outside the source scope when needed. Exit codes are 0 for complete analysis,
2 for unavailable/unsupported/partial/failed analysis, and 130 for cancellation.
Command-line syntax errors are handled by the parser. Cancellation waits for the
engine to stop; saved inventory and checkpoints remain in the run's index. This
CLI entry starts a new run each time; interactive pause/resume and operation
review remain workbench workflows.

## Measurements

Session time includes source preparation, inventory, analysis and result
validation, excluding command startup and native-library loading. I/O counters
cover the process, including SQLite and other concurrent work; they are not
physical disk-only measurements. Working-set samples and the process-lifetime
peak are labelled separately. Each run starts with a fresh FMA index; the OS
filesystem cache is not controlled. These fields support later comparison and
acceptance work, not an unmeasured claim that one engine is faster.

## Compare two configurations

```powershell
dotnet run --project src/Cli --no-build -- compare-engines C:\Samples --first csharp --second czkawka
dotnet run --project src/Cli --no-build -- compare-engines C:\Samples --first csharp --second csharp --first-algorithm xxh128-v1 --second-algorithm sha256-v1
```

Both configurations are validated before starting. They run sequentially with
separate durable indexes; groups are compared by their member paths rather than
by incompatible hash strings. The report contains both runs, measurements,
matching groups and groups found in only one run. Different file observations
produce `Incomparable`; a partial run never claims agreement. A second-run
preflight failure preserves the first report. Exit codes follow `analyze`.

The WPF Settings page offers the same read-only comparison when scans are idle
and the native engine is available. It freezes the configured online sources,
their exclusions, regenerable/cloud policies, protection, volume/root identity
and application-data exclusions for both runs. The CLI uses the defaults
described above. WPF saves a JSON report under the application's `comparisons`
folder; leaving Settings cancels the analysis. Cancellation also applies during
report validation/comparison. Completed index data remains available even when
the returned canceled report has only a partial inventory. The final atomic
report save finishes before the UI becomes idle.

Agreement between engines does not establish correctness. Each engine is also
checked against the shared known-answer fixture; see
[the comparison evidence](research/2026-10-24-engine-comparison.md). File
observations are checks at particular times, not an atomic filesystem snapshot.

## Similar-image candidates

```powershell
dotnet run --project src/Cli --no-build -- similar-images C:\Samples D:\Backup --distance 6
```

The initial image engine is `czkawka`, algorithm `image-mean8-d8-v1`; managed
image analysis is unsupported. A matching optional native library must be staged
as described above. `--distance` is the maximum Hamming distance, 0–16 bits,
default 6. Its version 1 JSON includes the independent analysis index, frozen
jobs, per-file perceptual evidence, candidate pairs and error diagnostics.
`exactGroups` is empty; no operation plan is produced. Exit codes follow `analyze`.

Only PNG/JPEG is enabled. Image count/pair limits, crop misses and the distinction
between a candidate and exact content are documented in
[Image review](image-review.md). The WPF page adds parallel viewing and persistent
manual notes; CLI JSON is a separate analysis format, not a WPF review archive.
