using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileWorkbench.Scanning;

internal static partial class AnalysisProcessMetrics
{
  internal sealed record Sample(ulong? Read, ulong? Write, long WorkingSet);

  internal static Sample Capture()
  {
    using var process = Process.GetCurrentProcess();
    var io = OperatingSystem.IsWindows() && GetProcessIoCounters(process.Handle, out var counters) ? counters : (IoCounters?)null;
    return new(io?.ReadBytes, io?.WriteBytes, process.WorkingSet64);
  }

  internal static AnalysisSessionMetrics Difference(Sample before, long elapsed)
  {
    var after = Capture();
    using var process = Process.GetCurrentProcess();
    return new(elapsed, Delta(before.Read, after.Read), Delta(before.Write, after.Write), before.WorkingSet, after.WorkingSet,
      process.PeakWorkingSet64, "Process counters include index I/O and other concurrent work; peak memory is process lifetime.",
      "Fresh File Workbench index; OS filesystem cache is not controlled.");
  }

  private static long? Delta(ulong? before, ulong? after) => before is { } start && after is { } end && end >= start
    ? (long)Math.Min(end - start, (ulong)long.MaxValue) : null;

  [StructLayout(LayoutKind.Sequential)]
  private struct IoCounters
  {
    public ulong ReadOperations;
    public ulong WriteOperations;
    public ulong OtherOperations;
    public ulong ReadBytes;
    public ulong WriteBytes;
    public ulong OtherBytes;
  }

  [LibraryImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool GetProcessIoCounters(nint processHandle, out IoCounters counters);
}
