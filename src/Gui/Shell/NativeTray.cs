using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using FileWorkbench.Localization;

namespace FileWorkbench.Gui.Shell;

/// <summary>
/// ADR-014: the app's notification-area icon and its keyboard-accessible native menu, as in desktop-work-fleet. Labels
/// and the tooltip come from the catalog, so the menu follows the display language each time it opens.
/// </summary>
public sealed class NativeTray : IDisposable
{
  private const int Callback = 0x8001;
  private const int Retries = 30;
  private const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetFocus = 3, NimSetVersion = 4;
  private readonly HwndSource _receiver;
  private readonly Action<string> _invoke;
  private readonly Func<IReadOnlyList<TrayEntry>> _entries;
  private readonly uint _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
  private readonly IntPtr _icon;
  private readonly bool _ownsIcon;
  // At sign-in the notification area can time out or not exist yet, so a failed add is retried every 2 s for a minute.
  private readonly DispatcherTimer _retry = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
  private int _attempts;
  private string _tip = AppText.Get("tray.tip");
  private bool _disposed;

  /// <param name="invoke">Runs a <see cref="TrayMenu"/> action on the UI thread.</param>
  /// <param name="entries">The menu rows for the state the menu opens in.</param>
  public NativeTray(Action<string> invoke, Func<IReadOnlyList<TrayEntry>> entries)
  {
    _invoke = invoke;
    _entries = entries;
    // The executable's embedded icon at notification-area size; the stock application icon only if none is embedded.
    var small = new IntPtr[1];
    _ownsIcon = Environment.ProcessPath is { } path && ExtractIconExW(path, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero;
    _icon = _ownsIcon ? small[0] : LoadIconW(IntPtr.Zero, new IntPtr(32512));
    // A hidden top-level window receives TaskbarCreated broadcasts; message-only windows do not.
    _receiver = new HwndSource(new HwndSourceParameters("File Workbench notification area")
    {
      Width = 0,
      Height = 0,
      WindowStyle = unchecked((int)0x80000000),
      ExtendedWindowStyle = 0x08000080,
    });
    _receiver.AddHook(Receive);
    _retry.Tick += (_, _) => Retry();
    Add();
  }

  public bool Available { get; private set; }

  /// <summary>Raised when the icon appears or goes away; a failed retry of an icon that was never there is not a change.</summary>
  public event EventHandler? Changed;

  /// <summary>An open menu does not repaint, so progress shows in the tooltip instead; null shows the usual tooltip.</summary>
  public void SetTip(string? text)
  {
    _tip = string.IsNullOrEmpty(text) ? AppText.Get("tray.tip") : text.Length > 120 ? text[..120] : text;
    if (_disposed || !Available)
    {
      return;
    }
    var value = Data();
    _ = Shell_NotifyIconW(NimModify, ref value);
  }

  private NotifyData Data() => new()
  {
    Size = (uint)Marshal.SizeOf<NotifyData>(),
    Window = _receiver.Handle,
    Id = 1,
    Flags = 1 | 2 | 4 | 0x80,
    Message = Callback,
    Icon = _icon,
    Tip = _tip,
    Info = string.Empty,
    Title = string.Empty,
    Version = 4,
  };

  private void Add()
  {
    if (_disposed)
    {
      return;
    }
    var was = Available;
    var value = Data();
    _ = Shell_NotifyIconW(NimDelete, ref value);
    Available = Shell_NotifyIconW(NimAdd, ref value);
    if (Available && !Shell_NotifyIconW(NimSetVersion, ref value))
    {
      _ = Shell_NotifyIconW(NimDelete, ref value);
      Available = false;
    }
    if (Available)
    {
      _retry.Stop();
    }
    else if (!_retry.IsEnabled)
    {
      _attempts = 0;
      _retry.Start();
    }
    if (Available != was)
    {
      Changed?.Invoke(this, EventArgs.Empty);
    }
  }

  /// <summary>TaskbarCreated still adds the icon when Explorer starts or restarts after the retries ended.</summary>
  private void Retry()
  {
    if (_disposed || Available || ++_attempts > Retries)
    {
      _retry.Stop();
      return;
    }
    Add();
  }

  private IntPtr Receive(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
  {
    if ((uint)message == _taskbarCreated)
    {
      Add();
      return IntPtr.Zero;
    }
    if (_disposed || message != Callback || ((lParam.ToInt64() >> 16) & 0xffff) != 1)
    {
      return IntPtr.Zero;
    }
    handled = true;
    var code = (int)(lParam.ToInt64() & 0xffff);
    if (code is 0x400 or 0x401)
    {
      Schedule(TrayMenu.Show); // NIN_SELECT / NIN_KEYSELECT
    }
    if (code == 0x007B)
    {
      Menu(wParam); // WM_CONTEXTMENU
    }
    return IntPtr.Zero;
  }

  private void Schedule(string action) =>
    _receiver.Dispatcher.BeginInvoke(() =>
    {
      if (!_disposed)
      {
        _invoke(action);
      }
    });

  private void Menu(IntPtr coordinates)
  {
    var menu = CreatePopupMenu();
    if (menu == IntPtr.Zero)
    {
      return;
    }
    try
    {
      var choices = _entries();
      for (var i = 0; i < choices.Count; i++)
      {
        var entry = choices[i];
        var added = entry.IsSeparator
          ? AppendMenuW(menu, 0x800, UIntPtr.Zero, null)
          : AppendMenuW(menu, 0, (UIntPtr)(i + 1), AppText.Get(entry.Text));
        if (!added)
        {
          return;
        }
        if (entry.Default)
        {
          _ = SetMenuDefaultItem(menu, (uint)(i + 1), 0);
        }
      }
      var packed = coordinates.ToInt64();
      var x = (int)(short)(packed & 0xffff);
      var y = (int)(short)((packed >> 16) & 0xffff);
      if (x == -1 && y == -1)
      {
        _ = GetCursorPos(out var point);
        x = point.X;
        y = point.Y;
      }
      _ = SetForegroundWindow(_receiver.Handle);
      var selected = TrackPopupMenuEx(menu, 0x100 | 0x2, x, y, _receiver.Handle, IntPtr.Zero);
      _ = PostMessageW(_receiver.Handle, 0, IntPtr.Zero, IntPtr.Zero);
      if (selected > 0 && selected <= choices.Count && !choices[(int)selected - 1].IsSeparator)
      {
        Schedule(choices[(int)selected - 1].Action);
      }
      else
      {
        var value = Data();
        _ = Shell_NotifyIconW(NimSetFocus, ref value);
      }
    }
    finally
    {
      _ = DestroyMenu(menu);
    }
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;
    _retry.Stop();
    var value = Data();
    _ = Shell_NotifyIconW(NimDelete, ref value);
    Available = false;
    _receiver.RemoveHook(Receive);
    _receiver.Dispose();
    if (_ownsIcon)
    {
      _ = DestroyIcon(_icon);
    }
  }

  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  private struct NotifyData
  {
    public uint Size;
    public IntPtr Window;
    public uint Id;
    public uint Flags;
    public uint Message;
    public IntPtr Icon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Tip;
    public uint State;
    public uint StateMask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Info;
    public uint Version;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string Title;
    public uint InfoFlags;
    public Guid Guid;
    public IntPtr BalloonIcon;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct Point
  {
    public int X;
    public int Y;
  }

  [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
  private static extern bool Shell_NotifyIconW(uint message, ref NotifyData value);

  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern uint RegisterWindowMessageW(string name);

  [DllImport("user32.dll")]
  private static extern IntPtr LoadIconW(IntPtr instance, IntPtr name);

  [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
  private static extern uint ExtractIconExW(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

  [DllImport("user32.dll")]
  private static extern bool DestroyIcon(IntPtr icon);

  [DllImport("user32.dll")]
  private static extern IntPtr CreatePopupMenu();

  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string? label);

  [DllImport("user32.dll")]
  private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);

  [DllImport("user32.dll")]
  private static extern bool DestroyMenu(IntPtr menu);

  [DllImport("user32.dll")]
  private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr options);

  [DllImport("user32.dll")]
  private static extern bool SetForegroundWindow(IntPtr window);

  [DllImport("user32.dll")]
  private static extern bool GetCursorPos(out Point point);

  [DllImport("user32.dll")]
  private static extern bool PostMessageW(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
