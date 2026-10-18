using Syphon.Engine;

internal static class MediaToolSecurityTests
{
    internal static int VerifyPackagedAssembly(string path)
    {
        var context = new System.Runtime.Loader.AssemblyLoadContext("packaged-media-policy-check", isCollectible: true);
        try
        {
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(path));
            var policy = assembly.GetType("Syphon.Engine.MediaToolResolver", throwOnError: true)!
                .GetProperty("IsPackagedBuild", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            if (policy.GetValue(null) is not true) throw new InvalidOperationException("Production assembly permits development media lookup.");
            Console.WriteLine($"PASS packaged media policy compiled into {assembly.Location}");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine($"FAIL packaged media policy: {ex}"); return 1; }
        finally { context.Unload(); }
    }

    internal static int Run(string[] args)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "media-security-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var passed = 0;
        var failed = 0;
        var skipped = 0;
        void Test(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine($"PASS {name}"); }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex}"); }
        }
        string Folder(string relative) { var path = Path.GetFullPath(Path.Combine(root, relative)); Directory.CreateDirectory(path); return path; }
        string Tool(string folder, string name = "ffmpeg")
        {
            var path = Path.Combine(folder, name + ".exe");
            File.WriteAllText(path, "fixture only; never executed");
            return path;
        }
        void Fails(Action test)
        {
            try { test(); }
            catch (IOException) { return; }
            throw new InvalidOperationException("Unsafe or missing bundle was accepted.");
        }
        void Equal(string actual, string expected)
        { if (actual != expected) throw new InvalidOperationException($"Unexpected tool: {actual}; expected {expected}"); }
        var application = Folder("app");
        var bundle = Folder("app/.tools/media");
        var hostile = Folder("hostile");
        var bundled = Tool(bundle);
        var overrideTool = Tool(hostile);
        Test("packaged bundle outranks malicious environment and PATH", () =>
            Equal(MediaToolResolver.Resolve("ffmpeg", true, application, hostile, [hostile]), bundled));
        Test("packaged missing bundle never falls back", () =>
            Fails(() => MediaToolResolver.Resolve("ffmpeg", true, Folder("missing"), hostile, [hostile])));
        Test("packaged lookup ignores executable beside application and ancestor bundle", () => {
            var nested = Folder("app/nested");
            Tool(nested);
            Fails(() => MediaToolResolver.Resolve("ffmpeg", true, nested, hostile, [hostile]));
        });
        Test("all four known tools resolve only inside bundle", () => {
            foreach (var name in new[] { "ffmpeg", "ffprobe", "yt-dlp", "node" })
            {
                var expected = Tool(bundle, name);
                Equal(MediaToolResolver.Resolve(name, true, application, hostile, [hostile]), expected);
            }
        });
        Test("invalid names rejected before filesystem lookup in both modes", () => {
            foreach (var packaged in new[] { true, false })
                foreach (var name in new[] { "../ffmpeg", @"..\ffmpeg", "ffmpeg.exe", "cmd", "", "FFmpeg", "C:/ffmpeg", "ffmpeg:alternate" })
                {
                    try { MediaToolResolver.Resolve(name, packaged, application, hostile, [hostile]); }
                    catch (ArgumentException) { continue; }
                    throw new InvalidOperationException($"Invalid tool name accepted: {name}");
                }
        });
        Test("development fixture override is preserved", () =>
            Equal(MediaToolResolver.Resolve("ffmpeg", false, application, hostile, []), overrideTool));
        Test("packaged relative base directory is rejected", () =>
            Fails(() => MediaToolResolver.Resolve("ffmpeg", true, "relative", hostile, [hostile])));
        Test("compiled policy and public lookup match selected build", () => {
            var expectPackaged = args.Contains("--expect-packaged");
            if (MediaToolResolver.IsPackagedBuild != expectPackaged)
                throw new InvalidOperationException($"Compiled packaged policy: {MediaToolResolver.IsPackagedBuild}; expected {expectPackaged}");
            var previous = Environment.GetEnvironmentVariable("SYPHON_MEDIA_DIRECTORY");
            var previousPath = Environment.GetEnvironmentVariable("PATH");
            try
            {
                Environment.SetEnvironmentVariable("SYPHON_MEDIA_DIRECTORY", hostile);
                Environment.SetEnvironmentVariable("PATH", hostile);
                if (expectPackaged)
                {
                    // Tests run in an isolated output directory; refuse to overwrite a real tool.
                    var actualBundle = Path.Combine(AppContext.BaseDirectory, ".tools", "media");
                    Directory.CreateDirectory(actualBundle);
                    var actualTool = Path.Combine(actualBundle, "ffmpeg.exe");
                    if (File.Exists(actualTool)) throw new InvalidOperationException("Use fresh isolated test output; a bundled ffmpeg already exists.");
                    try
                    {
                        Tool(actualBundle);
                        Equal(MediaTools.FindTool("ffmpeg"), actualTool);
                        File.Delete(actualTool);
                        Fails(() => MediaTools.FindTool("ffmpeg"));
                    }
                    finally { File.Delete(actualTool); }
                }
                else Equal(MediaTools.FindTool("ffmpeg"), overrideTool);
            }
            finally
            {
                Environment.SetEnvironmentVariable("SYPHON_MEDIA_DIRECTORY", previous);
                Environment.SetEnvironmentVariable("PATH", previousPath);
            }
        });
        void LinkTest(string name, string link, string target, bool directory, Action verify)
        {
            try
            {
                if (directory) Directory.CreateSymbolicLink(link, target);
                else File.CreateSymbolicLink(link, target);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            { skipped++; Console.WriteLine($"SKIP {name}: symbolic links unavailable ({ex.GetType().Name})"); return; }
            try { Test(name, verify); }
            finally { if (directory) Directory.Delete(link); else File.Delete(link); }
        }
        var linkedFileBundle = Folder("linked-file/.tools/media");
        LinkTest("packaged linked executable rejected", Path.Combine(linkedFileBundle, "ffmpeg.exe"), overrideTool, false,
            () => Fails(() => MediaToolResolver.Resolve("ffmpeg", true, Path.Combine(root, "linked-file"), hostile, [hostile])));
        var linkedDirectoryApp = Folder("linked-directory/.tools");
        LinkTest("packaged linked media directory rejected", Path.Combine(linkedDirectoryApp, "media"), hostile, true,
            () => Fails(() => MediaToolResolver.Resolve("ffmpeg", true, Path.Combine(root, "linked-directory"), hostile, [hostile])));
        LinkTest("packaged linked application ancestor rejected", Path.Combine(root, "linked-app"), application, true,
            () => Fails(() => MediaToolResolver.Resolve("ffmpeg", true, Path.Combine(root, "linked-app"), hostile, [hostile])));
        Console.WriteLine($"{passed}/{passed + failed} passed, {skipped} skipped; fixtures: {root}");
        return failed == 0 ? 0 : 1;
    }
}
