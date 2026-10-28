using System.Reflection;
using System.IO;
using System.Text.Json;
using Syphon.Desktop;

namespace Syphon.Desktop.SmokeTests;

internal static class FirstUpdateTests
{
    public static int Run()
    {
        try {
            var assembly = typeof(MainWindow).Assembly;
            var preferences = assembly.GetType("Syphon.Desktop.DownloadPreferences")!;
            var temporary = Path.Combine(Path.GetTempPath(), "Syphon-first-update-" + Guid.NewGuid().ToString("N"));
            var defaults = preferences.GetMethod("Load")!.Invoke(null, [temporary])!;
            if (!(bool)preferences.GetProperty("StartWithWindows")!.GetValue(defaults)! || (bool)preferences.GetProperty("PreventSleep")!.GetValue(defaults)! || (string)preferences.GetProperty("CompletionAction")!.GetValue(defaults)! != "None") throw new Exception("Startup or power-action defaults are incorrect.");
            var integration = assembly.GetType("Syphon.Desktop.WindowsIntegration")!;
            var startup = (string)integration.GetMethod("StartupCommand", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, ["C:\\Program Files\\Syphon\\Syphon.exe"])!;
            if (startup != "\"C:\\Program Files\\Syphon\\Syphon.exe\" --startup") throw new Exception("Startup path quoting is incorrect.");
            var updates = assembly.GetType("Syphon.Desktop.ReleaseUpdates") ?? throw new Exception("Release discovery is missing.");
            var select = updates.GetMethod("SelectRelease")!;
            string Releases(string tag, string url = "https://github.com/HaggaiMakungo/syphon/releases/tag/v0.1.0-beta.18") => JsonSerializer.Serialize(new[] { new { tag_name = tag, html_url = url, draft = false, prerelease = true, body = "release notes" } });
            object? Pick(string json, string version = "0.1.0-beta.17") => select.Invoke(null, [json, version, null]);
            if (Pick(Releases("v0.1.0-beta.18")) is null) throw new Exception("New beta was not found.");
            if (Pick(Releases("v0.1.0-beta.9")) is not null) throw new Exception("Version ordering is lexical.");
            if (Pick(Releases("v0.1.0-beta.18", "https://evil.example/update.exe")) is not null) throw new Exception("Untrusted update URL accepted.");
            if (Pick(Releases("v0.1.0-beta.18"), "0.1.0") is not null) throw new Exception("Stable build offered beta downgrade.");
            if (select.Invoke(null, [Releases("v0.1.0-beta.18"), "0.1.0-beta.17", "v0.1.0-beta.18"]) is not null) throw new Exception("Skipped release was offered.");
            var diagnostics = assembly.GetType("Syphon.Desktop.DiagnosticsExport") ?? throw new Exception("Diagnostics export is missing.");
            var clean = (string)diagnostics.GetMethod("Redact")!.Invoke(null, ["https://user:password@host.test/file?token=secret C:\\Users\\Jane\\Downloads\\private.bin Cookie: abc"])!;
            if (new[] { "password", "token=secret", "Jane", "Cookie: abc" }.Any(clean.Contains)) throw new Exception("Diagnostics exposed private data: " + clean);
            var privateLog = (string)diagnostics.GetMethod("Redact")!.Invoke(null, ["{\"token\":\"private-value\",\"access_token\":\"private-access\"}\napi_key=private-key\nBearer private-bearer"])!;
            if (new[] { "private-value", "private-access", "private-key", "private-bearer" }.Any(privateLog.Contains)) throw new Exception("Structured credentials leaked in diagnostics.");
            var idle = assembly.GetType("Syphon.Desktop.QueueIdleAction") ?? throw new Exception("Queue idle guard is missing.");
            var update = idle.GetMethod("Observe")!; var policy = Activator.CreateInstance(idle)!;
            var now = DateTimeOffset.UtcNow;
            object? Observe(bool busy, bool blocked, bool completed, int seconds = 0) => update.Invoke(policy, [busy, blocked, completed, now.AddSeconds(seconds), 30]);
            if (Observe(false, false, false) is not null) throw new Exception("Empty startup armed power action.");
            Observe(true, false, false);
            if (Observe(false, true, false) is not null) throw new Exception("Failed/paused work armed power action.");
            Observe(true, false, false);
            if (Observe(false, false, true) is null) throw new Exception("Successful drained queue did not arm countdown.");
            if (Observe(true, false, false, 1) is not null) throw new Exception("New work failed to cancel countdown.");
            Console.WriteLine("PASS first update: release ordering/origin/skip, diagnostics privacy, idle-action guards."); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
