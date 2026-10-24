# Offscreen WPF scale diagnostic

On Windows x64 with the .NET 10 SDK and PowerShell 7.2+, run:

```powershell
.\scripts\windows\Invoke-GuiScale.ps1
```

The runner builds Debug and starts one owned child with a 180-second runtime
limit (configurable). It retains events, process status, binary hashes, measured
JSON and a finite 1440x900 screenshot under a new `build/gui-scale-<id>` tree.
Failure, timeout or incomplete reports cannot pass. It never opens a visible
window, installs, executes a plan, scans user files or closes another File Workbench.

The model is synthetic, derived from in-memory demo data: 10,001 stacks, a
10,000-row folder comparison and all 10,000 conflicts. The existing 200-row
comparison-summary limit remains. Initial hub display shows its existing 20
items. The probe measures initial view creation, filtering, sorting and 12
scroll jumps; records process memory and queued Dispatcher Input-priority delay;
and asserts bounded controls, retained outcome models, zero binding errors and
first/last conflict-radio choices surviving container recycling.

It also executes all 500 load-more commands to expose every hub model, checks
all groups keep fewer than 100 containers, reaches each final row and opens the
correct stack through its recycled button. Filtering that fully loaded component
must reset to 20 matching file models. The total load-more time is a synchronous
500-command burst with one final render, not one user click or physical input
latency. Existing paging semantics and all models are retained.

It also checks named focusable list/container entries. It cannot acquire actual
keyboard focus in an invisible desktop and does not inject physical keys. In
the disposable GUI validation, use Tab/Shift+Tab to enter/leave both lists,
arrows/PageDown/Home/End to reach later conflicts/results, and Space to change
one conflict option. Return to the first/last options after scrolling and confirm
their choices; record inaccessible rows, missing focus cues or accidental choice
changes as failures. Verify high DPI and a screen reader separately.

These are single Debug diagnostics on a development host with background work,
not physical input latency, concurrent real scan acceptance, release thresholds
or a claim that the full workbench is ready for arbitrary collection sizes.
