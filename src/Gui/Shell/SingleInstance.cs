using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace FileWorkbench.Gui.Shell;

/// <summary>
/// ADR-014: one File Workbench per user and data folder, as in desktop-work-fleet. A second launch asks the
/// running one to show its main window and exits, so a window waiting in the tray is never out of reach.
/// </summary>
public sealed class SingleInstance : IDisposable
{
  private readonly string _name;
  private readonly CancellationTokenSource _stop = new();
  private readonly object _gate = new();
  private Action? _show;
  private bool _pending;

  private SingleInstance(string name)
  {
    _name = name;
    _ = Task.Run(Serve);
  }

  /// <summary>True when a running instance for this data folder accepted the request to show itself.</summary>
  public static bool Forward(string directory)
  {
    try
    {
      using var client = new NamedPipeClientStream(".", PipeName(directory), PipeDirection.InOut, PipeOptions.None);
      client.Connect(500);
      // This process was started by the user, so it may hand the foreground to the running instance.
      if (GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(), out var process))
      {
        _ = AllowSetForegroundWindow(process);
      }
      client.Write("show\n"u8);
      client.Flush();
      var reply = new byte[3];
      var read = 0;
      var deadline = DateTime.UtcNow.AddSeconds(5);
      while (read < reply.Length && DateTime.UtcNow < deadline)
      {
        var task = client.ReadAsync(reply, read, reply.Length - read);
        if (!task.Wait(TimeSpan.FromSeconds(5)) || task.Result == 0)
        {
          break;
        }
        read += task.Result;
      }
      return read == 3 && reply.AsSpan().SequenceEqual("ok\n"u8);
    }
    catch (Exception error) when (error is TimeoutException or IOException or UnauthorizedAccessException or AggregateException)
    {
      return false;
    }
  }

  /// <summary>Starts answering later launches; call only after this process owns the data folder.</summary>
  public static SingleInstance Serve(string directory) => new(PipeName(directory));

  /// <summary>Requests that arrived before the main window was ready are replayed once it is.</summary>
  public void Listen(Action handler)
  {
    bool replay;
    lock (_gate)
    {
      _show = handler;
      replay = _pending;
      _pending = false;
    }
    if (replay)
    {
      handler();
    }
  }

  private static string PipeName(string directory)
  {
    var owner = WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
    var digest = SHA256.HashData(Encoding.UTF8.GetBytes(owner + "\n" + Path.GetFullPath(directory).ToUpperInvariant()));
    // Shared with published FMA builds: an upgrade must not open the same index twice.
    return "FileMergeAssistant-" + Convert.ToHexString(digest)[..32];
  }

  private async Task Serve()
  {
    var security = new PipeSecurity();
    var owner = WindowsIdentity.GetCurrent().User;
    if (owner is not null)
    {
      security.AddAccessRule(new PipeAccessRule(owner, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
    }
    while (!_stop.IsCancellationRequested)
    {
      try
      {
        using var server = NamedPipeServerStreamAcl.Create(_name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 64, 64, security);
        await server.WaitForConnectionAsync(_stop.Token);
        var buffer = new byte[16];
        var read = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        while (read < 5)
        {
          var count = await server.ReadAsync(buffer.AsMemory(read, buffer.Length - read), timeout.Token);
          if (count == 0)
          {
            break;
          }
          read += count;
        }
        if (read != 5 || !buffer.AsSpan(0, 5).SequenceEqual("show\n"u8))
        {
          continue;
        }
        Action? handler;
        lock (_gate)
        {
          handler = _show;
          if (handler is null)
          {
            _pending = true;
          }
        }
        handler?.Invoke();
        await server.WriteAsync("ok\n"u8.ToArray(), timeout.Token);
        await server.FlushAsync(timeout.Token);
      }
      catch (OperationCanceledException) when (_stop.IsCancellationRequested)
      {
        return;
      }
      catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException)
      {
        await Task.Delay(200);
      }
    }
  }

  /// <summary>Stops answering; the token source stays alive for the listening task, which ends with the process.</summary>
  public void Dispose() => _stop.Cancel();

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint process);

  [DllImport("user32.dll")]
  private static extern bool AllowSetForegroundWindow(uint process);
}
