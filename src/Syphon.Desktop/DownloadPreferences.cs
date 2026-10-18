using System.IO;
using System.Text.Json;
using Syphon.Engine;

namespace Syphon.Desktop;

internal sealed record DownloadPreferences(string? Folder = null, bool DefaultSingleConnection = false, bool ShowFloatingWindow = true, bool FloatingOnTop = true, TransferOptions? Transfer = null, string HistoryDensity = "comfortable", bool HistoryGroupAttempts = false, string TypographyPreset = "hybrid", bool CloseToTray = false, bool MinimizeToTray = false)
{
    public static DownloadPreferences Load(string path)
    {
        try {
            var preferences = JsonSerializer.Deserialize<DownloadPreferences>(File.ReadAllText(path)) ?? new();
            if (preferences.HistoryDensity is not ("comfortable" or "compact")) preferences = preferences with { HistoryDensity = "comfortable" };
            if (!TypographyComparison.IsValid(preferences.TypographyPreset)) preferences = preferences with { TypographyPreset = "hybrid" };
            try { preferences.Transfer?.Validate(); } catch (ArgumentException) { preferences = preferences with { Transfer = null }; }
            if (preferences.Folder is not null) {
                if (!Path.IsPathFullyQualified(preferences.Folder)) return preferences with { Folder = null };
                _ = Path.GetFullPath(preferences.Folder);
            }
            return preferences;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException) { return new(); }
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, path, true);
    }
}
