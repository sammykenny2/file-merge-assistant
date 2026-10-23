using CommunityToolkit.Mvvm.Input;

namespace FileMergeAssistant.Gui.Workbench;

/// <summary>Colour and icon of a status chip; never the only carrier of meaning, the chip always has text.</summary>
public enum Tone
{
  /// <summary>Grey, no icon.</summary>
  Plain,

  /// <summary>Fine: check mark.</summary>
  Ok,

  /// <summary>Work in progress: accent with the sync icon.</summary>
  Busy,

  /// <summary>Offline: dashed border with the unlink icon.</summary>
  Offline,

  /// <summary>Needs attention: amber with the warning icon.</summary>
  Warn,

  /// <summary>Waiting: clock icon.</summary>
  Wait,

  /// <summary>Paused: pause icon.</summary>
  Paused,

  /// <summary>Done: accent ink with a check mark.</summary>
  Done,

  /// <summary>Not run this time: dash icon.</summary>
  Skipped,

  /// <summary>Protected: lock icon.</summary>
  Protected,
}

/// <summary>A small labelled status chip.</summary>
public sealed record ChipInfo(string Text, Tone Tone);

/// <summary>A labelled button bound to an action, for lists whose actions differ per item.</summary>
public sealed record ActionInfo(string Label, IRelayCommand Command, bool IsPrimary = false);
