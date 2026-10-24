using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using FileWorkbench.Localization;

namespace FileWorkbench.Gui.Localization;

/// <summary>
/// The catalog as a binding source: <c>{Binding [key], Source={x:Static l:Loc.Instance}}</c>, or <c>{l:T key}</c>.
/// When the display language changes, every such binding reads its text again, so the window switches at once.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
  private Loc()
  {
    AppText.Changed += (_, _) =>
    {
      // Apply runs on the UI thread from the settings page; anything else is marshalled there.
      var dispatcher = Application.Current?.Dispatcher;
      if (dispatcher is null || dispatcher.CheckAccess())
      {
        Raise();
      }
      else
      {
        dispatcher.BeginInvoke(Raise);
      }
    };
  }

  public static Loc Instance { get; } = new();

  public event PropertyChangedEventHandler? PropertyChanged;

  public string this[string key] => AppText.Get(key);

  /// <summary>
  /// The space between a verb and what it acts on, or a name and its kind: an ideographic space in Chinese, an en space
  /// in English. A catalog entry cannot be only white space.
  /// </summary>
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Bound with change notification.")]
  public string Gap => AppText.Locale == "zh-TW" ? "\u3000" : "\u2002";

  private void Raise()
  {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Gap)));
  }
}

/// <summary>XAML text from the catalog that follows the display language: <c>Text="{l:T hub.title}"</c>.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class T : MarkupExtension
{
  public T()
  {
  }

  public T(string key) => Key = key;

  [ConstructorArgument("key")]
  public string Key { get; set; } = string.Empty;

  public override object ProvideValue(IServiceProvider serviceProvider) =>
    new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
