using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Syphon.Desktop;

/// <summary>Separate shadow siblings keep the material face and its content crisp.</summary>
internal static class MaterialPanel
{
    public static Grid Raised(UIElement content, Thickness padding, double radius = 16)
    {
        var grid = new Grid();
        foreach (var shadow in new[] { "RaisedLight", "RaisedDark" }) grid.Children.Add(new Border {
            Background = Brush("Card"), CornerRadius = new(radius), IsHitTestVisible = false,
            Effect = (Effect)Application.Current.FindResource(shadow) });
        grid.Children.Add(new Border { Background = Brush("Card"), CornerRadius = new(radius), Padding = padding, Child = content });
        return grid;
    }
    public static Border Inset(UIElement content, Thickness padding, double radius = 12) => new() {
        Background = Brush("Inset"), CornerRadius = new(radius), Padding = padding, Child = content,
        BorderThickness = new(1), BorderBrush = Brush("InsetEdge") };
    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
