using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Workbench;

namespace FileMergeAssistant.Gui.Workbench;

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
      Title = "沒有新的變化";
      Kicker = string.Empty;
      return;
    }

    var days = (since.Now.Date - since.LastVisit.Date).Days;
    Kicker = $"{Fmt.Day(since.Now, since.Now)}，距離上次整理 {days} 天";
    Title = $"自 {Fmt.Day(since.LastVisit, since.Now)} 以來";
    DemoNote = Store.IsDemo ? "示範畫面：用模擬資料呈現一週後回來時會看到的內容，日期是模擬的。" : null;
    Sections.Add(new NoticeSection("需要你看一下", since.Notices.Where(n => n.NeedsAttention).Select(ToViewModel).ToList()));
    Sections.Add(new NoticeSection("只是讓你知道", since.Notices.Where(n => !n.NeedsAttention).Select(ToViewModel).ToList()));

    CountTitle = $"待整理：{Fmt.Count(since.OpenNow)} 疊";
    CountStart = $"上次開始時是 {Fmt.Count(since.OpenAtLastVisit)} 疊。";
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
        actions.Add(new ActionInfo("是同一顆，開始更新", new RelayCommand(() => Respond(NoticeResponse.SameDrive)), true));
        actions.Add(new ActionInfo("不是，當成新的來源", new RelayCommand(() => Respond(NoticeResponse.NewSource))));
        break;
      case NoticeKind.NewCopy:
        actions.Add(new ActionInfo("打開這疊", new RelayCommand(() => OpenStack(notice)), true));
        actions.Add(new ActionInfo("不屬於這疊", new RelayCommand(() => Respond(NoticeResponse.NotInStack))));
        break;
      case NoticeKind.KeptAllChanged:
        actions.Add(new ActionInfo("重新看看", new RelayCommand(() => OpenStack(notice))));
        actions.Add(new ActionInfo("維持都保留", new RelayCommand(() => Respond(NoticeResponse.KeepAllAgain))));
        break;
      case NoticeKind.StackReady:
        actions.Add(new ActionInfo("打開這疊", new RelayCommand(() => OpenStack(notice)), true));
        break;
      case NoticeKind.RecycleReminder:
        actions.Add(new ActionInfo("檢視回收區", new RelayCommand(() => Navigator.ShowHistory())));
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
