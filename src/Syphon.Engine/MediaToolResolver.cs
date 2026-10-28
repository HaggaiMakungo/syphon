namespace Syphon.Engine;

/// <summary>Media lookup policy, with explicit inputs so environment cannot weaken packaged builds.</summary>
internal static class MediaToolResolver
{
    internal static bool IsPackagedBuild =>
#if SYPHON_PACKAGED_BUILD
        true;
#else
        false;
#endif

    internal static string Resolve(string name, bool packagedBuild, string baseDirectory,
        string? developmentDirectory, IEnumerable<string> pathDirectories)
    {
        if (name is not ("ffmpeg" or "ffprobe" or "yt-dlp" or "node"))
            throw new ArgumentException("Unknown media tool.", nameof(name));

        if (packagedBuild)
        {
            if (!Path.IsPathFullyQualified(baseDirectory))
                throw new IOException("The application media bundle path is invalid.");
            var file = Path.GetFullPath(Path.Combine(baseDirectory, ".tools", "media", name + ".exe"));
            if (IsRegularBundledFile(file)) return file;
            throw new IOException($"The bundled {name} tool is missing or unsafe. Repair or reinstall Syphon.");
        }

        // Explicitly compiled development mode retains fixture overrides and local tool discovery.
        var local = new List<string>();
        for (var parent = new DirectoryInfo(baseDirectory); parent is not null; parent = parent.Parent)
            local.Add(Path.Combine(parent.FullName, ".tools", "media"));
        var candidates = new[] { developmentDirectory, baseDirectory, @"C:\ffmpeg\bin", @"C:\Program Files\nodejs" }
            .Concat(local).Concat(pathDirectories);
        foreach (var folder in candidates)
            if (!string.IsNullOrWhiteSpace(folder))
            {
                var file = Path.Combine(folder.Trim('"'), name + ".exe");
                if (File.Exists(file)) return Path.GetFullPath(file);
            }
        throw new IOException(name switch {
            "yt-dlp" => "Video page support needs yt-dlp. Run install-youtube.ps1 in the Syphon folder.",
            "node" => "YouTube support needs Node.js 22 or later. Install it or place node.exe on PATH.",
            _ => $"{name} is required for media processing. Install FFmpeg and FFprobe, or set SYPHON_MEDIA_DIRECTORY to their folder." });
    }

    private static bool IsRegularBundledFile(string file)
    {
        try
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) return false;
            // Reject junctions/symlinks anywhere in the executable's path, including application ancestors.
            for (var directory = new FileInfo(file).Directory; directory is not null; directory = directory.Parent)
                if ((File.GetAttributes(directory.FullName) & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return false; }
    }
}
