# Isolated scan/restore test process

This executable is a test fixture, never part of the GUI/CLI release package.
Core.Tests builds it through a project reference and copies its runtime files to
`scan-host/` under the test output. Run the tests with
`scripts/windows/Build-NativeAnalysis.ps1 -Test`; do not run the host against a
personal index.

`interrupt` computes one cohort through the actual Rust adapter, then blocks the
next upload in the test-only cancellation DLL. Its marker proves both the owned
child PID and a 200-file persisted checkpoint. The parent terminates only that
child/tree, then verifies WAL recovery, unchanged snapshots and original active
hashes, unavailable-engine diagnostics and resume using the original engine.
This simulates process loss, not an assertion that native aborts are catchable.

`restore` starts fresh with an explicitly absent native installation directory.
The parent removes native-test environment variables; the child verifies no File Workbench
native module is loaded both before and after restoring the journalled batch.
This covers history/restore without an already-resident Rust library. All paths
belong to the tests' temporary source trees and isolated index directory.
