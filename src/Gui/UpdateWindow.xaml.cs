using System.Windows;
using FileMergeAssistant.Gui.ViewModels;

namespace FileMergeAssistant.Gui;

public partial class UpdateWindow : Window
{
  public UpdateWindow(UpdateViewModel viewModel)
  {
    InitializeComponent();
    DataContext = viewModel;
    viewModel.ConfirmInstall = package => MessageBox.Show(
      this,
      $"Version {package.Version.ToString(3)} is ready to install. File Merge Assistant will close, update and reopen.\n\n" +
      "Make sure no scan or cleanup is running. Install now?",
      "Install update",
      MessageBoxButton.YesNo,
      MessageBoxImage.Question) == MessageBoxResult.Yes;
    // Opening the window is the user's request to check, so the check starts right away.
    Loaded += async (_, _) => await viewModel.CheckCommand.ExecuteAsync(null);
    // Closing the window abandons a running check or download; nothing is installed afterwards.
    Closing += (_, _) => viewModel.CancelPendingWork();
  }
}
