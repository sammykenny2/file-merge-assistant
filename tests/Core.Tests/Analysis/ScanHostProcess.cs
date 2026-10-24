using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace FileWorkbench.Tests.Analysis;

/// <summary>Owns only the test child it starts. It never discovers or terminates other processes.</summary>
internal sealed class ScanHostProcess : IAsyncDisposable
{
  private readonly Process _process;
  private readonly Task<string> _output;
  private readonly Task<string> _error;

  private ScanHostProcess(Process process)
  {
    _process = process;
    _output = process.StandardOutput.ReadToEndAsync();
    _error = process.StandardError.ReadToEndAsync();
  }

  public int Id => _process.Id;

  public static ScanHostProcess Start(string command, string index, string argument, string other, string report)
  {
    var host = Path.Combine(AppContext.BaseDirectory, "scan-host", "FileWorkbench.ScanHost.dll");
    Assert.True(File.Exists(host), "Build Core.Tests and its ScanHost project reference before running process tests.");
    var start = new ProcessStartInfo("dotnet")
    {
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      WorkingDirectory = Path.GetDirectoryName(host)!,
    };
    foreach (var item in new[] { host, command, index, argument, other, report }) { start.ArgumentList.Add(item); }
    start.Environment["FWB_DATA_DIR"] = Path.GetDirectoryName(Path.GetFullPath(index))!;
    start.Environment["DOTNET_NOLOGO"] = "1";
    if (command == "restore")
    {
      start.Environment.Remove("FWB_NATIVE_LIBRARY_DIR");
      start.Environment.Remove("FWB_WRONG_ABI_LIBRARY_DIR");
      start.Environment.Remove("FWB_CANCEL_UPLOAD_LIBRARY_DIR");
    }
    return new(Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated test host."));
  }

  public async Task<JsonElement> Marker(string path)
  {
    var deadline = Stopwatch.StartNew();
    while (deadline.Elapsed < TimeSpan.FromSeconds(30))
    {
      if (File.Exists(path))
      {
        try { return JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(path)); }
        catch (JsonException) { }
        catch (IOException) { }
      }
      if (_process.HasExited) { Assert.Fail($"Test host exited before its marker: {await _output}\n{await _error}"); }
      await Task.Delay(10);
    }
    Assert.Fail("Timed out waiting for the isolated test host marker.");
    return default;
  }

  public async Task ExitSuccessfully()
  {
    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
    Assert.True(_process.ExitCode == 0, $"Test host failed: {await _output}\n{await _error}");
  }

  public async Task Terminate()
  {
    if (!_process.HasExited)
    {
      try { _process.Kill(entireProcessTree: true); }
      catch (InvalidOperationException) when (_process.HasExited) { }
    }
    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
  }

  public async ValueTask DisposeAsync()
  {
    try
    {
      await Terminate();
      await Task.WhenAll(_output, _error);
    }
    finally { _process.Dispose(); }
  }
}
