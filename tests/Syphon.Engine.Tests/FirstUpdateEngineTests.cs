using Syphon.Engine;
internal static class FirstUpdateEngineTests
{
    public static int Run()
    {
        if (DownloadSafety.RequiredSpace(100, 25, true) != 175) throw new Exception("Assembly reserve must include remaining segments and complete output");
        try { DownloadSafety.EnsureSpace("C:/", 100, _ => 99); throw new Exception("Low space accepted"); }
        catch (IOException ex) when (ex.Message.Contains("Free up")) { }
        if (MediaTags.Clean("Hi\0\nthere") != "Hithere" || MediaTags.Clean(new string('x', 2000)).Length != 512) throw new Exception("Unsafe tags accepted");
        var adaptive = new AdaptiveConnections(4);
        if (adaptive.Limit != 1 || adaptive.Sample(100) != 2 || adaptive.Sample(180) != 3 || adaptive.Sample(180) != 3 || adaptive.Sample(300) != 3) throw new Exception("Plateau did not stop growth");
        adaptive.BackOff(); if (adaptive.Limit != 1) throw new Exception("Backoff did not reduce allocation");
                using var metadata = System.Text.Json.JsonDocument.Parse("{\"title\":\"Track\\u0000name\",\"artist\":\"Artist\",\"album\":\"Album\",\"track_number\":3,\"release_year\":2026}");
        var tags = MediaTags.FromJson(metadata.RootElement);
        if (tags["title"] != "Trackname" || tags["track"] != "3" || tags["date"] != "2026") throw new Exception("Source metadata not mapped");
        var path = Path.Combine(Path.GetTempPath(), "Syphon.Mark." + Guid.NewGuid().ToString("N"));
        try {
            File.WriteAllText(path, "download bytes");
            var before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
            DownloadSafety.Mark(path, "https://user:secret@example.com/file?q=1", "https://example.com/page");
            if (!before.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))) throw new Exception("Mark changed final digest");
            if (OperatingSystem.IsWindows()) {
                var zone = File.ReadAllText(path + ":Zone.Identifier");
                if (!zone.Contains("ZoneId=3") || zone.Contains("secret") || !zone.Contains("ReferrerUrl=")) throw new Exception("Unsafe or missing download zone");
            }
        } finally { File.Delete(path); }
        Console.WriteLine("PASS first-update engine space, tags, adaptive allocation, publication zone and unchanged digest"); return 0;
    }
    public static async Task<int> MediaAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "Syphon.Tags." + Guid.NewGuid().ToString("N") + ".mp3");
        try {
            await MediaTools.RunAsync("ffmpeg", ["-nostdin", "-hide_banner", "-n", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-f", "lavfi", "-i", "color=c=blue:s=32x32:d=1", "-map", "0:a", "-map", "1:v", "-c:v", "mjpeg", "-frames:v", "1", "-disposition:v", "attached_pic", "-c:a", "libmp3lame", "-metadata", "title=Source\u0001title", path], null, default);
            await MediaTags.ApplyAsync(path, null, default);
            var fallbackJson = await MediaTools.RunAsync("ffprobe", ["-v", "error", "-show_entries", "format_tags", "-of", "json", path], null, default);
            using (var fallback = System.Text.Json.JsonDocument.Parse(fallbackJson))
                if (fallback.RootElement.GetProperty("format").GetProperty("tags").GetProperty("title").GetString() != "Sourcetitle") throw new Exception("Null-tag source fallback did not sanitize source title");
            await MediaTags.ApplyAsync(path, new Dictionary<string,string> { ["title"]="Track\0name", ["artist"]="Artist", ["album"]="Album", ["track"]="3", ["date"]="2026" }, default);
            var json = await MediaTools.RunAsync("ffprobe", ["-v", "error", "-show_entries", "format_tags", "-show_streams", "-of", "json", path], null, default);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (!document.RootElement.GetProperty("streams").EnumerateArray().Any(stream => stream.GetProperty("codec_type").GetString() == "video" && stream.GetProperty("disposition").GetProperty("attached_pic").GetInt32() == 1)) throw new Exception("Tagging lost cover art");
            var tags = document.RootElement.GetProperty("format").GetProperty("tags");
            foreach (var pair in new[] { ("title","Trackname"), ("artist","Artist"), ("album","Album"), ("track","3"), ("date","2026") })
                if(tags.GetProperty(pair.Item1).GetString()!=pair.Item2) throw new Exception("MP3 tag mismatch: " + pair.Item1);
            await MediaTags.ApplyAsync(path, null, default); // Existing tool metadata is also accepted.
            Console.WriteLine("PASS actual MP3 ID3 title/artist/album/track/year remux, preserved cover art and source fallback"); return 0;
        } finally { File.Delete(path); }
    }}




