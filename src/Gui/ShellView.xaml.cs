using System.Windows.Controls;

namespace FileWorkbench.Gui;

/// <summary>The window body: header, status strip and the current screen. Separate from the window so it can be rendered in tests.</summary>
public partial class ShellView : UserControl
{
  public ShellView()
  {
    InitializeComponent();
  }
}
