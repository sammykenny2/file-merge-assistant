# Windows synthetic analysis baseline — 2026-10-24

PLAN-002 Phase 5 uses the provisional ADR-011 scales; the ADR remains Proposed.
Only generated data was used. No personal-data statistics were collected.

Host: Windows 11 Pro build 26300, i9-13900HX (32 logical processors), 34,065,997,824
bytes RAM (31.7 GiB), NTFS fixed C: volume. .NET SDK 10.0.402; FMA Core Debug
at `0f2696e` with the then-uncommitted acceptance host; native adapter 0.2.0,
Rust 1.99.1 Release, upstream `eb8b91dbb4d25dff416202674ee0de84ee4b5e5c`.
Suite JSON records the actual host/Core/native binary SHA-256 values.
This long suite uses the frozen `0f2696e` host/Core copies and the original
runner loaded at launch. The later bounded runner (`1e05021`) and portable
assembly extraction (`c6592ae`) have separate tests; this run does not measure
their rebuilt binaries.

## Completed cases

| Primary files | Engine | Fresh index (s) | Warm (s) | +1% files (s) | Exact groups |
|---:|---|---:|---:|---:|---:|
| 5,000 | C# XxHash128 | 10.919 | 14.250 | 14.292 | 253 |
| 5,000 | Czkawka BLAKE3 | 9.793 | 20.201 | 15.410 | 253 |
| 50,000 | C# XxHash128 | 88.621 | 104.717 | 128.671 | 2,503 |
| 50,000 | Czkawka BLAKE3 | 96.452 | 141.846 | 138.619 | 2,503 |
| 300,000 | C# XxHash128 | 479.041 | 673.619 | 747.831 | 15,003 |

Each case has six sources, 19 extra indexed entries, 600 measured regenerable
files and six excluded files. All completed cases match the independent
content-family oracle, preserve source bytes, skip the junction and pass
simulated offline waiting/inventory retention/resume. Warm backend inputs are
zero; +1% backend inputs are exactly 50, 500 or 3,000. This verifies content-cache reuse,
not that a warm full workbench rescan is faster.

The 50k first populated model notification arrived after 7.841 s (C#) and 6.943 s
(native). This is not a rendered/interactive GUI measurement. Sampled whole-tool
working-set peaks stayed below 192 MB in these runs. Index growth is material:
after three phases the main DB was 405,704,704 bytes (C#) / 458,428,416 bytes
(native), plus WAL/SHM. Complete evidence generations are retained; no retention
policy or destructive history cleanup was added by this test.

The completed C# 300k case also passed simulated offline recovery and retained
all 303,019 indexed entries after the +1% phase. At that phase's boundary the
main DB was 2,434,420,736 bytes, WAL 127,031,992 bytes, and sampled process working
set 617,742,336 bytes. Process-reported read transfers were 53,299,341,832 bytes
for that rescan; these include cached/SQLite activity and are not physical disk
reads. This is a material indexing/retained-generation cost, not evidence that
hash-library speed alone determines application performance.

Raw retained evidence:

- `build/acceptance-a7042713cbe2421894bb72c89aa5fe26/suite.json` (5k)
- `build/acceptance-9a329d6eb5114d6fb320fb387adc5771/suite.json` (50k; 300k continues in that suite)

## Interpretation and remaining cases

Both engines are correct on this corpus. Warm/full rescan time includes repeated
metadata/scope checks, evidence and SQLite work; eliminating hash requests alone
did not reduce the observed total. The measurements do not isolate a single
bottleneck or justify changing the default engine.

These are single diagnostic runs on a development host. OS cache was not purged;
generation/verification warm it, and small development builds/tests and unrelated
host activity were not isolated. .NET is Debug while Rust is Release. Do not use
the table as an optimized speed comparison or performance guarantee.

The Czkawka 300k case is still running and is not counted as passing here. The
separate [offscreen WPF probe](2026-10-24-wpf-scale.md) measures synthetic UI
layout/recycling, not input during a real scan. Physical USB/HDD, controlled
cold disk cache, physical GUI input, full hub load-more and
clean-VM lifecycle acceptance remain unverified. Managed XxHash128 stays the
default; both engines remain Experimental and G5 is not passed.

Reproduction and metric definitions are in
[the acceptance host](../../tests/acceptance-host/README.md). The bounded process
runner also passed normal dual-output and sustained dual-output timeout probes;
empty and duplicate matrices are rejected before any build.
