using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using FileWorkbench.Sources;

namespace FileWorkbench.AnalysisVerifier;

internal sealed record VolumeProof(string? Identity, string Method, string Detail)
{
  public static async Task<VolumeProof> ObserveAsync(string directory)
  {
    var method = OperatingSystem.IsWindows() ? "Win32 volume GUID" : "system stat st_dev";
    try
    {
      if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
      { return new(null, method, "The verification root is a link; no volume proof claimed."); }
      if (OperatingSystem.IsWindows())
      {
        var guid = FileIdentityReader.Identify(directory)?.VolumeGuid;
        return new(guid?.ToLowerInvariant(), method, guid is null ? "No volume GUID available; serial numbers alone are not used as distinct-volume proof." : "OS volume identity; does not prove separate physical disks.");
      }
      if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) { return new(null, method, "No volume probe for this OS."); }
      var executable = File.Exists("/usr/bin/stat") ? "/usr/bin/stat" : "/bin/stat";
      var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
      start.ArgumentList.Add(OperatingSystem.IsMacOS() ? "-f" : "-c");
      start.ArgumentList.Add("%d"); start.ArgumentList.Add(directory);
      using var process = Process.Start(start) ?? throw new IOException("Could not start the system stat tool.");
      using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
      var errors = process.StandardError.ReadToEndAsync(deadline.Token);
      try
      {
        await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        var value = (await output.ConfigureAwait(false)).Trim();
        var error = await errors.ConfigureAwait(false);
        if (process.ExitCode != 0 || value.Length > 32 || !long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var device) || device == 0)
        { return new(null, method, "stat did not return a usable device ID: " + error[..Math.Min(error.Length, 512)]); }
        return new("unix-device:" + device.ToString(CultureInfo.InvariantCulture), method, "OS device identity; not a physical-disk or file-identity provider.");
      }
      catch (OperationCanceledException)
      {
        try { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
        catch (InvalidOperationException) { }
        using var stopDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await process.WaitForExitAsync(stopDeadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return new(null, method, "stat timeout; kill requested but exit not observed. No volume proof claimed."); }
        return new(null, method, "The owned stat process exceeded five seconds; no volume proof claimed.");
      }
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
    { return new(null, method, error.Message); }
  }
}
