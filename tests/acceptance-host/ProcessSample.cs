using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileMergeAssistant.AcceptanceHost;

internal sealed record ProcessSample(ulong ReadBytes, ulong WriteBytes, long WorkingSet, double CpuMilliseconds)
{
  public static ProcessSample Capture()
  {
    using var process = Process.GetCurrentProcess();
    if (!NativeCounters.GetProcessIoCounters(process.Handle, out var counters)) { throw new IOException("Process I/O counters are unavailable."); }
    return new(counters.ReadBytes, counters.WriteBytes, process.WorkingSet64, process.TotalProcessorTime.TotalMilliseconds);
  }
}

internal static partial class NativeCounters
{
  [StructLayout(LayoutKind.Sequential)]
  internal struct IoCounters
  {
    public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
  }
  [LibraryImport("kernel32.dll", SetLastError = true)]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  [return: MarshalAs(UnmanagedType.Bool)]
  internal static partial bool GetProcessIoCounters(nint process, out IoCounters counters);
}
