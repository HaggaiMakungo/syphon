namespace Syphon.Engine;

/// <summary>Stable destination categories for new downloads; existing tasks never move.</summary>
public static class DownloadCategories
{
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[] { "Videos", "Music", "Documents", "Programs", "Misc" });
    public static string Category(string filename, string? mediaFormat = null)
    {
        var extension = mediaFormat is null ? Path.GetExtension(filename).ToLowerInvariant() : "." + mediaFormat.ToLowerInvariant();
        return extension switch {
            ".mp4" or ".mkv" or ".webm" or ".avi" or ".mov" or ".wmv" or ".flv" or ".m4v" or ".mpg" or ".mpeg" or ".ts" or ".m2ts" or ".3gp" or ".m3u8" or ".mpd" => "Videos",
            ".mp3" or ".m4a" or ".aac" or ".flac" or ".wav" or ".ogg" or ".opus" or ".wma" or ".aiff" or ".alac" => "Music",
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".rtf" or ".csv" or ".odt" or ".ods" or ".odp" or ".epub" or ".md" or ".srt" or ".vtt" => "Documents",
            ".exe" or ".msi" or ".msix" or ".msixbundle" or ".appx" or ".appxbundle" or ".msp" or ".com" => "Programs",
            _ => "Misc"
        };
    }
    public static string Resolve(string root, string filename, string? mediaFormat = null)
        => Path.Combine(Path.GetFullPath(root), Category(filename, mediaFormat));
    public static void EnsureFolders(string root)
    {
        foreach (var category in Names) Directory.CreateDirectory(Path.Combine(Path.GetFullPath(root), category));
    }
}
