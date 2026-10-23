using System.Windows.Controls;

namespace FileMergeAssistant.Gui.Workbench;

public partial class AddSourcesView : UserControl
{
  public AddSourcesView()
  {
    InitializeComponent();
  }
}

/// <summary>One numbered step in 「加入之後會發生什麼」.</summary>
public sealed class HowStep
{
  public string Number { get; set; } = string.Empty;
  public string Title { get; set; } = string.Empty;
  public string Text { get; set; } = string.Empty;
}
