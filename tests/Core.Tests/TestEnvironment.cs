using System.Runtime.CompilerServices;
using FileWorkbench.Infrastructure;
using FileWorkbench.Localization;

namespace FileWorkbench.Tests;

internal static class TestEnvironment
{
  /// <summary>Keeps every default data path under a throwaway folder so no test writes to the real %LOCALAPPDATA%.</summary>
  public static readonly string DataRoot = Path.Combine(Path.GetTempPath(), $"file-workbench-test-data-{Environment.ProcessId}");

  [ModuleInitializer]
  internal static void Initialize()
  {
    Environment.SetEnvironmentVariable(AppDataPaths.DataDirectoryVariable, DataRoot);
    // Messages the tests compare are in Traditional Chinese, whatever language this Windows uses.
    AppText.Apply("zh-TW", []);
  }
}
