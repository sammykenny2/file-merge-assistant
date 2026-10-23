# Versioned analysis CLI

`engines` and `analyze` use the same registry, engine snapshots, source policies
and result validator as the WPF workbench. They currently expose exact-content
analysis. The initial managed algorithm is `xxh128-v1`; native Czkawka uses
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
