# Windows analysis acceptance host

Developer-only generated-data tool. It is not shipped with File Workbench and never runs an
operation plan. Run from the repository root with PowerShell 7.2+, .NET 10 and
the pinned native toolchain:

```powershell
.\scripts\windows\Invoke-AnalysisAcceptance.ps1 -FileCounts 5000
.\scripts\windows\Invoke-AnalysisAcceptance.ps1 -FileCounts 50000,300000
```

All outputs are retained under a new ignored `build/acceptance-<id>` tree. The
script refuses empty/duplicate matrices, records each child before starting it,
drains both output streams fairly and enforces a whole-case deadline (120 minutes
by default, configurable with `-CaseTimeoutMinutes`). It only terminates the
child tree it created. A crash, timeout or missing/invalid report cannot pass.
No recursive cleanup is performed.

Each case creates six protected sources using seed 173. The nominal count is
primary files; six project markers, twelve synthetic PNG copies and a hardlink
add 19 indexed entries. Six excluded `.tmp` files and 600 regenerable files are
also created. Ten percent of primary entries form cross-source duplicate pairs;
headers encode independent content-family IDs. Unicode, paths over 260 characters
and a skipped source junction are included. Expected groups are derived from
the generator's families, not from either engine's hashes.

Fresh-index, same-index warm and +1% new-primary-file phases assert complete jobs,
full inventory, known groups, exclusions and unchanged source bytes. Warm expects
zero backend inputs; +1% expects only the new files. A final same-volume directory
rename verifies WaitingForSource, retained inventory and resume. That is not a
physical USB/HDD test.

Measurements include queue/scanning wall time, separately timed model refresh,
the first populated model notification, sampled phases/working set, process CPU
and I/O, logical backend bytes and DB/WAL sizes. Content verification is timed
separately. Memory covers the entire tool, including SQLite and oracle metadata.
Phase samples have approximately 100 ms resolution. First model notification is
not first rendered GUI response; GUI input latency and scrolling remain unmeasured.

The application is Debug and the selected native binary is built Release by the
wrapper. OS cache and other development-host activity are uncontrolled. These
are provisional diagnostic measurements, not optimized speed rankings, formal
acceptance thresholds, or representative personal-data distributions. The large
fixture can take time and multiple GB; leave the machine awake. The script does
not alter power settings.
