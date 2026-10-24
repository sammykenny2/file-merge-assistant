namespace FileWorkbench.Gui.Shell;

/// <summary>One tray menu row: an action and its catalog key, or a separator when Action is empty.</summary>
/// <param name="Default">The item the icon's own click runs, drawn bold.</param>
public sealed record TrayEntry(string Action, string Text, bool Default = false)
{
  public static readonly TrayEntry Separator = new(string.Empty, string.Empty);

  public bool IsSeparator => Action.Length == 0;
}

/// <summary>
/// ADR-014: the tray menu for the state it opens in, grouped the way Windows menus are. Opening the app comes first and
/// bold, as the icon's click does it; hiding follows only while the main window shows. Then checking for updates where
/// this copy can update itself, and exiting last.
/// </summary>
public static class TrayMenu
{
  public const string Show = "show";
  public const string Hide = "hide";
  public const string Update = "update";
  public const string Exit = "exit";

  public static IReadOnlyList<TrayEntry> Entries(bool mainVisible, bool updates)
  {
    var entries = new List<TrayEntry> { new(Show, "tray.open", Default: true) };
    if (mainVisible)
    {
      entries.Add(new TrayEntry(Hide, "tray.hideMain"));
    }
    entries.Add(TrayEntry.Separator);
    if (updates)
    {
      entries.Add(new TrayEntry(Update, "tray.checkUpdates"));
      entries.Add(TrayEntry.Separator);
    }
    entries.Add(new TrayEntry(Exit, "tray.exit"));
    return entries;
  }
}
