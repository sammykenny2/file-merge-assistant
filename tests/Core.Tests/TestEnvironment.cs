using System.Runtime.CompilerServices;
using FileMergeAssistant.Infrastructure;

namespace FileMergeAssistant.Tests;

internal static class TestEnvironment
{
  /// <summary>Keeps every default data path under a throwaway folder so no test writes to the real %LOCALAPPDATA%.</summary>
  public static readonly string DataRoot = Path.Combine(Path.GetTempPath(), $"fma-test-data-{Environment.ProcessId}");

  [ModuleInitializer]
  internal static void Initialize()
  {
    Environment.SetEnvironmentVariable(AppDataPaths.DataDirectoryVariable, DataRoot);
  }
}
