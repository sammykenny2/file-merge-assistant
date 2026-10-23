using System.IO;
using FileMergeAssistant.Gui.Settings;
using FileMergeAssistant.Gui.Shell;
using FileMergeAssistant.Gui.Updates;
using FileMergeAssistant.Infrastructure;
using FileMergeAssistant.Updates;

namespace FileMergeAssistant.Gui.Tests;

/// <summary>A settings context over a throwaway data folder, in-memory startup keys and a scripted update source.</summary>
internal static class TestSettings
{
  public static SettingsContext Create(
    string? directory = null,
    FakeStartupKeys? keys = null,
    FakeUpdateService? updates = null,
    bool installed = true,
    bool tray = true,
    bool demo = false)
  {
    directory ??= Path.Combine(Path.GetTempPath(), "fma-gui-settings-" + Guid.NewGuid().ToString("N"));
    keys ??= new FakeStartupKeys();
    var shell = new HostShell(directory, launchedAtLogin: false,
      new StartupRegistration(keys, installed ? @"C:\Programs\FileMergeAssistant\app\FileMergeAssistant.exe" : null));
    var coordinator = new UpdateCoordinator(updates ?? new FakeUpdateService(), installed, "owner/repo", shell.LastUpdateCheck, shell.RecordUpdateCheck);
    return new SettingsContext(shell, coordinator, installed ? "installed" : "debug", () => tray, demo);
  }
}

internal sealed class FakeStartupKeys : IStartupKeys
{
  public Dictionary<string, string> Run { get; } = new(StringComparer.OrdinalIgnoreCase);

  public Dictionary<string, byte[]> Approval { get; } = new(StringComparer.OrdinalIgnoreCase);

  public string? ReadRun(string name) => Run.GetValueOrDefault(name);

  public void WriteRun(string name, string command) => Run[name] = command;

  public void DeleteRun(string name) => Run.Remove(name);

  public byte[]? ReadApproval(string name) => Approval.GetValueOrDefault(name);

  public bool Applied { get; set; }

  public bool ReadApplied() => Applied;

  public void WriteApplied() => Applied = true;
}

/// <summary>Answers checks and downloads from what the test set up, and counts them.</summary>
internal sealed class FakeUpdateService : IUpdateService
{
  public UpdatePackage? Package { get; set; }

  public Exception? CheckFailure { get; set; }

  public TaskCompletionSource? Gate { get; set; }

  public int Checks { get; private set; }

  public int Downloads { get; private set; }

  public static UpdatePackage NewPackage(string version) => new(
    Version.Parse(version), "v" + version, "Faster scans.", $"FileMergeAssistant-{version}-x64-setup.exe", 10,
    new string('a', 64), new Uri("https://github.com/owner/repo/releases/download/v" + version + "/setup.exe"),
    new Uri("https://github.com/owner/repo/releases/tag/v" + version));

  public async Task<UpdatePackage?> CheckAsync(Version current, CancellationToken cancellationToken = default)
  {
    Checks++;
    if (Gate is { } gate)
    {
      await gate.Task;
    }
    if (CheckFailure is { } failure)
    {
      throw failure;
    }
    return Package;
  }

  public Task<string> DownloadAsync(UpdatePackage package, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
  {
    Downloads++;
    var path = Path.Combine(Path.GetTempPath(), "fma-gui-update-" + Guid.NewGuid().ToString("N") + ".exe");
    File.WriteAllText(path, "installer");
    return Task.FromResult(path);
  }
}
