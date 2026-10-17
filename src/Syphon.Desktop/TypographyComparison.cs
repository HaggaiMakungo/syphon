using System.IO;
using System.Text.RegularExpressions;

namespace Syphon.Desktop;

/// <summary>Presentation-only typography preferences shared by all frontend windows.</summary>
internal static class TypographyComparison
{
    public static string Current { get; private set; } = "hybrid";
    public static event Action? Changed;
    public static bool IsValid(string? preset) => preset is "inter" or "plex" or "hybrid";
    public static void Apply(string preset) {
        if (!IsValid(preset)) throw new ArgumentException("Choose one of the three interface typography presets.");
        if (Current == preset) return;
        Current = preset; Changed?.Invoke();
    }
    public static bool IsFontAsset(string name) => name is "inter-400.woff2" or "inter-500.woff2" or "inter-600.woff2"
        or "plex-400.woff2" or "plex-500.woff2" or "plex-600.woff2" or "space-500-600.woff2";
    public static string InlineCss() {
        var folder = Path.Combine(AppContext.BaseDirectory, "DesktopAssets");
        return Regex.Replace(File.ReadAllText(Path.Combine(folder, "typography.css")), @"url\(([^)]+\.woff2)\)", match => {
            var name = match.Groups[1].Value;
            if (!IsFontAsset(name)) throw new InvalidDataException("Unexpected typography asset.");
            return "url(data:font/woff2;base64," + Convert.ToBase64String(File.ReadAllBytes(Path.Combine(folder, name))) + ")";
        });
    }
}
