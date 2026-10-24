using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileWorkbench.Localization;
using FileWorkbench.Workbench;

namespace FileWorkbench.Gui.Workbench;

public enum NoticeIcon
{
  Drive,
  Copy,
  Change,
  Ready,
  Less,
  More,
  Recycle,
}

public sealed record NoticeViewModel(
  string Title,
  string Description,
  NoticeIcon Icon,
  bool IsAmber,
  bool IsGrey,
  IReadOnlyList<ActionInfo> Actions,
  string? HandledText)
{
  public bool IsHandled => HandledText is not null;
  public bool HasActions => !IsHandled && Actions.Count > 0;
}

public sealed record NoticeSection(string Title, IReadOnlyList<NoticeViewModel> Items);

/// <summary>Screen 7, 「一週後回來」: what changed since the last visit, needs-a-look first.</summary>
public sealed partial class SinceViewModel : PageViewModel
{
  [ObservableProperty]
  private string _kicker = string.Empty;

  [ObservableProperty]
  private string _title = string.Empty;

  [ObservableProperty]
  private string _countTitle = string.Empty;

  [ObservableProperty]
  private string _countStart = string.Empty;

  [ObservableProperty]
  private string? _demoNote;

  public SinceViewModel(IWorkbenchStore store, INavigator navigator)
    : base(store, navigator)
  {
    Refresh();
  }

  public ObservableCollection<NoticeSection> Sections { get; } = [];
  public ObservableCollection<OpenStackDelta> Deltas { get; } = [];

  public override void Refresh()
  {
    Sections.Clear();
    Deltas.Clear();
    if (Store.SinceLastVisit is not { } since)
    {
      Title = AppText.Get("since.none");
      Kicker = string.Empty;
      return;
    }

    var days = (since.Now.Date - since.LastVisit.Date).Days;
    Kicker = AppText.Plural("since.kicker", days, ("day", Fmt.Day(since.Now, since.Now)), ("days", days));
    Title = AppText.Get("since.title", ("day", Fmt.Day(since.LastVisit, since.Now)));
    DemoNote = Store.IsDemo ? AppText.Get("since.demoNote") : null;
    Sections.Add(new NoticeSection(AppText.Get("since.section.attention"), since.Notices.Where(n => n.NeedsAttention).Select(ToViewModel).ToList()));
    Sections.Add(new NoticeSection(AppText.Get("since.section.info"), since.Notices.Where(n => !n.NeedsAttention).Select(ToViewModel).ToList()));

    CountTitle = AppText.Plural("since.open", since.OpenNow, ("count", Fmt.Count(since.OpenNow)));
    CountStart = AppText.Plural("since.openAtStart", since.OpenAtLastVisit, ("count", Fmt.Count(since.OpenAtLastVisit)));
    foreach (var delta in since.Deltas)
    {
      Deltas.Add(delta);
    }
  }

  [RelayCommand]
  private void GoToHub() => Navigator.ShowHub();

  private NoticeViewModel ToViewModel(ChangeNotice notice)
  {
    var actions = new List<ActionInfo>();
    void Respond(NoticeResponse response) => Store.RespondToNotice(notice.Id, response);
    switch (notice.Kind)
    {
      case NoticeKind.DriveReconnected:
        actions.Add(new ActionInfo(AppText.Get("since.action.sameDrive"), new RelayCommand(() => Respond(NoticeResponse.SameDrive)), true));
        actions.Add(new ActionInfo(AppText.Get("since.action.newSource"), new RelayCommand(() => Respond(NoticeResponse.NewSource))));
        break;
      case NoticeKind.NewCopy:
        actions.Add(new ActionInfo(AppText.Get("since.action.openStack"), new RelayCommand(() => OpenStack(notice)), true));
        actions.Add(new ActionInfo(AppText.Get("since.action.notInStack"), new RelayCommand(() => Respond(NoticeResponse.NotInStack))));
        break;
      case NoticeKind.KeptAllChanged:
        actions.Add(new ActionInfo(AppText.Get("since.action.lookAgain"), new RelayCommand(() => OpenStack(notice))));
        actions.Add(new ActionInfo(AppText.Get("since.action.keepAll"), new RelayCommand(() => Respond(NoticeResponse.KeepAllAgain))));
        break;
      case NoticeKind.StackReady:
        actions.Add(new ActionInfo(AppText.Get("since.action.openStack"), new RelayCommand(() => OpenStack(notice)), true));
        break;
      case NoticeKind.RecycleReminder:
        actions.Add(new ActionInfo(AppText.Get("since.action.viewRecycle"), new RelayCommand(() => Navigator.ShowHistory())));
        break;
    }
    var icon = notice.Kind switch
    {
      NoticeKind.DriveReconnected => NoticeIcon.Drive,
      NoticeKind.NewCopy => NoticeIcon.Copy,
      NoticeKind.KeptAllChanged => NoticeIcon.Change,
      NoticeKind.StackReady => NoticeIcon.Ready,
      NoticeKind.CopyLost => NoticeIcon.Less,
      NoticeKind.SourceGrew => NoticeIcon.More,
      _ => NoticeIcon.Recycle,
    };
    var amber = notice.Kind is NoticeKind.NewCopy or NoticeKind.KeptAllChanged;
    return new NoticeViewModel(notice.Title, notice.Description, icon, amber, !notice.NeedsAttention, actions, notice.HandledText);
  }

  private void OpenStack(ChangeNotice notice)
  {
    if (notice.StackId is not null && Store.Stacks.Any(s => s.Id == notice.StackId))
    {
      Navigator.ShowStack(notice.StackId);
    }
  }
}
