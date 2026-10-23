using System.Windows;
using System.Windows.Interop;

namespace FileMergeAssistant.Gui;

public partial class MainWindow : Window
{
  private const int WmSysCommand = 0x0112;
  private const int ScClose = 0xF060;

  public MainWindow()
  {
    InitializeComponent();
    SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnMessage);
  }

  /// <summary>Whether the close command hides the window to the tray instead of closing it (ADR-014).</summary>
  public Func<bool>? CloseToTray { get; set; }

  private IntPtr OnMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
  {
    // X, Alt+F4 and the taskbar's close command (WM_SYSCOMMAND/SC_CLOSE) keep the app in the tray. Exit in the tray
    // menu and shutting the app down still close the window and quit.
    if (message == WmSysCommand && (wParam.ToInt64() & 0xFFF0) == ScClose && CloseToTray?.Invoke() == true)
    {
      handled = true;
      Hide();
    }
    return IntPtr.Zero;
  }
}
