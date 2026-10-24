using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileWorkbench.Gui.Workbench;

/// <summary>A small pile of sheets, one per copy (up to five); the front sheet is the accent colour.</summary>
public sealed class StackGlyph : Canvas
{
  public static readonly DependencyProperty CountProperty = DependencyProperty.Register(
    nameof(Count), typeof(int), typeof(StackGlyph), new PropertyMetadata(2, (d, _) => ((StackGlyph)d).Draw()));

  public static readonly DependencyProperty IsLargeProperty = DependencyProperty.Register(
    nameof(IsLarge), typeof(bool), typeof(StackGlyph), new PropertyMetadata(false, (d, _) => ((StackGlyph)d).Draw()));

  public StackGlyph()
  {
    Draw();
  }

  public int Count
  {
    get => (int)GetValue(CountProperty);
    set => SetValue(CountProperty, value);
  }

  public bool IsLarge
  {
    get => (bool)GetValue(IsLargeProperty);
    set => SetValue(IsLargeProperty, value);
  }

  private void Draw()
  {
    Children.Clear();
    var sheets = Math.Clamp(Count, 1, 5);
    var step = IsLarge ? 5.0 : 3.0;
    var width = IsLarge ? 26.0 : 16.0;
    var height = IsLarge ? 32.0 : 20.0;
    Width = width + step * (sheets - 1) + 2;
    Height = height + step * (sheets - 1) + 2;
    var accent = (Brush)Application.Current.FindResource("AccentBrush");
    var accentSoft = (Brush)Application.Current.FindResource("AccentSoftBrush");
    var hint = (Brush)Application.Current.FindResource("HintBrush");
    var surface = (Brush)Application.Current.FindResource("SurfaceBrush");
    for (var i = 0; i < sheets; i++)
    {
      var front = i == sheets - 1;
      var sheet = new Border
      {
        Width = width,
        Height = height,
        BorderThickness = new Thickness(1.5),
        CornerRadius = new CornerRadius(2),
        BorderBrush = front ? accent : hint,
        Background = front ? accentSoft : surface,
      };
      SetLeft(sheet, (sheets - 1 - i) * step);
      SetTop(sheet, i * step);
      Children.Add(sheet);
    }
  }
}
