using System.Windows.Controls;

namespace FileMergeAssistant.Gui.Workbench;

public partial class AddSourcesView : UserControl
{
  public AddSourcesView()
  {
    InitializeComponent();
  }
}

/// <summary>One numbered step in 「加入之後會發生什麼」; <see cref="AddSourcesViewModel"/> builds them in the display language.</summary>
public sealed record HowStep(string Number, string Title, string Text);
