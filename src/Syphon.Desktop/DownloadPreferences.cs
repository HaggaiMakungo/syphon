using System.IO;
using System.Text.Json;
using Syphon.Engine;

namespace Syphon.Desktop;

internal sealed record DownloadPreferences(string? Folder = null, bool DefaultSingleConnection = false, bool ShowFloatingWindow = true, bool FloatingOnTop = true, TransferOptions? Transfer = null, string HistoryDensity = "comfortable", bool HistoryGroupAttempts = false, string TypographyPreset = "hybrid", bool CloseToTray = true, bool MinimizeToTray = false,
    bool StartWithWindows = true, bool CompletionNotifications = true, bool PreventSleep = false, string CompletionAction = "None", int CompletionCountdownSeconds = 30,
    string CompletionCommand = "", string DuplicateChoice = "ask", bool CheckForUpdates = true, string? SkippedRelease = null)
{
    public static DownloadPreferences Load(string path)
    {
        try {
            var preferences = JsonSerializer.Deserialize<DownloadPreferences>(File.ReadAllText(path)) ?? new();
            if (preferences.CompletionAction is not ("None" or "Sleep" or "Hibernate" or "Shutdown" or "Command")) preferences = preferences with { CompletionAction = "None" };
            if (preferences.CompletionCountdownSeconds is < 10 or > 300) preferences = preferences with { CompletionCountdownSeconds = 30 };
            if (preferences.DuplicateChoice is not ("ask" or "numbered" or "replace" or "existing")) preferences = preferences with { DuplicateChoice = "ask" };
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
