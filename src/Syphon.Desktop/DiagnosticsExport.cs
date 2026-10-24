using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Diagnostics;
using Syphon.Engine;

namespace Syphon.Desktop;

public static class DiagnosticsExport
{
    internal static async Task<Dictionary<string, string>> ToolVersionsAsync()
    {
        var result = new Dictionary<string, string>();
        foreach (var name in new[] { "ffmpeg", "ffprobe", "yt-dlp", "node" }) {
            try {
                using var process = new Process { StartInfo = new ProcessStartInfo(MediaTools.FindTool(name), name is "ffmpeg" or "ffprobe" ? "-version" : "--version") {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
                process.Start();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                // Drain both streams; FFmpeg can fill stderr before it exits.
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var errors = process.StandardError.ReadToEndAsync(timeout.Token);
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } throw; }
                var text = (await output + "\n" + await errors).Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "No version reported";
                result[name] = Redact(text[..Math.Min(text.Length, 240)]);
            } catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception) { result[name] = "Unavailable"; }
        }
        return result;
    }
    public static string Redact(string value)
    {
        value = Regex.Replace(value, "(?im)\\b[a-z0-9_-]*(?:cookie|authorization|password|token|secret|api[_-]?key)[\"']?\\s*[:=][^\\r\\n]*", "[credentials redacted]");
        value = Regex.Replace(value, @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+", "[authorization redacted]");
        value = Regex.Replace(value, @"(?i)https?://[^\s\""<>]+", match => Uri.TryCreate(match.Value, UriKind.Absolute, out var url)
            ? url.GetLeftPart(UriPartial.Authority).Replace(url.UserInfo + "@", "", StringComparison.Ordinal) + "/[path redacted]" : "[URL redacted]");
        value = Regex.Replace(value, @"(?i)\b[A-Z]:[\\/][^\r\n\""<>]*", "[local path redacted]");
        value = Regex.Replace(value, @"\\\\[^\s\r\n]+", "[network path redacted]");
        return value;
    }
    internal static string Create(string preferencesPath, IEnumerable<string> errors, IDictionary<string, string>? tools)
    {
        var preferences = DownloadPreferences.Load(preferencesPath);
        return Redact("Syphon diagnostics\nVersion: " + ReleaseUpdates.CurrentVersion + "\nOS: " + Environment.OSVersion +
            "\nSettings: " + JsonSerializer.Serialize(preferences with { Folder = null, CompletionCommand = "[redacted]" }) +
            "\nBundled tools: " + JsonSerializer.Serialize(tools) + "\nRecent errors:\n" + string.Join("\n", errors.TakeLast(30)));
    }
}
