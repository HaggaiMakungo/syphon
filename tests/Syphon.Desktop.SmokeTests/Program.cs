using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Syphon.Desktop;
using Syphon.TestServer;
using Syphon.Core;
using Syphon.Engine;
using Syphon.BrowserBridge;
using System.Diagnostics;
using System.Text.Json;

namespace Syphon.Desktop.SmokeTests;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--duplicates") return DuplicateSmokeTests.Run();
        if (args.FirstOrDefault() == "--first-update") return FirstUpdateTests.Run();
        if (args.FirstOrDefault() == "--shared-floating") return SharedFloatingSmokeTests.Run();
        if (args.FirstOrDefault() == "--tray") return TraySmokeTests.Run();
        if (args.FirstOrDefault() == "--typography") return TypographySmokeTests.Run(args.Skip(1).ToArray());
        if (args.FirstOrDefault() == "--history-audit") {
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
            using var db = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder {DataSource=args[1],Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
            db.Open();using var command=db.CreateCommand();command.CommandText="SELECT snapshot FROM tasks";using var reader=command.ExecuteReader();
            while(reader.Read()){var task=JsonSerializer.Deserialize<StoredDownload>(reader.GetString(0))!;if(task.State is DownloadState.Failed or DownloadState.Cancelled)Console.WriteLine(JsonSerializer.Serialize(new {task.Id,State=task.State.ToString(),task.Received,task.MediaFormat,task.YoutubeFormat,task.Message,task.Diagnostic}));}
            return 0;
        }
        if (args.FirstOrDefault() == "--floating-input") return FloatingInputSmokeTests.Run(args.Skip(1).ToArray());
        if (args.FirstOrDefault() == "--folder") return FolderSmokeTests.Run();
        if (args.FirstOrDefault() == "--details") return DetailsSmokeTests.Run(args.Skip(1).ToArray());
        if (args.FirstOrDefault() == "--size-display") return SizeDisplaySmokeTests.Run();
        if (args.FirstOrDefault() == "--history-layout") return HistoryLayoutSmokeTests.Run(args.Skip(1).ToArray());
        if (args.FirstOrDefault() == "--dashboard-load") return DashboardLoadSmokeTests.Run(args.Skip(1).ToArray());
        if (args.FirstOrDefault() == "--html") return HtmlSmokeTests.Run(args.Skip(1).ToArray());
        if (args.FirstOrDefault() == "--options") return OptionsSmokeTests.Run(args.Skip(1).ToArray());
        if (args.FirstOrDefault() == "--redesign") return RedesignSmokeTests.Run(args.Skip(1).ToArray());
        if (args.FirstOrDefault() == "--obsidian") return ObsidianVisualTests.Run(args.Skip(1).ToArray());
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/desktop");
        Directory.CreateDirectory(output);
        var temporary = Path.Combine(Path.GetTempPath(), "Syphon.UI.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // Exercise and render our own visual tree off-screen, without moving user windows.
        var store = Path.Combine(temporary, "store");
        var pipeName = "Syphon.UI." + Guid.NewGuid().ToString("N");
        var window = new MainWindow(store, pipeName, htmlUi: false) { Left = -10000, Top = -10000, ShowInTaskbar = false };
        var exitCode = 1;
        window.Loaded += async (_, _) =>
        {
            var fixture = await FixtureServer.StartAsync();
            try
            {
                T Control<T>(string name) => (T)window.FindName(name);
                StoredDownload? Selected() => (Control<DataGrid>("TaskList").SelectedItem as DownloadRow)?.Snapshot;
                await WaitUntil(() => window.IsReady);
                foreach (var name in new[] { "DownloadButton", "PauseButton", "ResumeButton", "CancelButton", "RestartButton", "OpenFolderButton", "RemoveButton" })
                {
                    var button = Control<Button>(name);
                    if (string.IsNullOrWhiteSpace(new ButtonAutomationPeer(button).GetName()) || !button.IsTabStop)
                        throw new InvalidOperationException($"Unnamed or unreachable command: {name}");
                }
                if (string.IsNullOrWhiteSpace(new CheckBoxAutomationPeer(Control<CheckBox>("SingleConnectionChoice")).GetName()) ||
                    System.Windows.Automation.AutomationProperties.GetLiveSetting(Control<TextBlock>("StatusText")) != System.Windows.Automation.AutomationLiveSetting.Polite)
                    throw new InvalidOperationException("Connection choice or status announcements are not accessible.");
                var url = Control<TextBox>("UrlBox");
                var destination = Control<TextBox>("DestinationBox");
                var download = Control<Button>("DownloadButton");
                var cancel = Control<Button>("CancelButton");
                var status = Control<TextBlock>("StatusText");
                Capture(window, Path.Combine(output, "ready.png"));
                var file = Path.Combine(temporary, "verified.bin");
                url.Text = new Uri(fixture.BaseAddress, "file?size=2097153").AbsoluteUri;
                destination.Text = file;
                download.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => Selected()?.State == DownloadState.Completed);
                if (status.Text != "Download complete." || new FileInfo(file).Length != 2097153)
                    throw new InvalidOperationException($"Desktop download failed: {status.Text}");
                using (var input = File.OpenRead(file))
                    if (Convert.ToHexStringLower(SHA256.HashData(input)) != Payload.ExpectedHash(2097153))
                        throw new InvalidOperationException("Desktop output bytes differ.");
                Capture(window, Path.Combine(output, "completed.png"));

                download.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => Selected()?.State == DownloadState.Failed);
                if (!status.Text.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Desktop did not explain the destination collision.");

                destination.Text = Path.Combine(temporary, "cancelled.bin");
                url.Text = new Uri(fixture.BaseAddress, "resume?size=8388608").AbsoluteUri;
                destination.Text = Path.Combine(temporary, "resumed.bin");
                download.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => Selected()?.Received > 131072);
                Control<Button>("PauseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => Selected()?.State == DownloadState.Paused);
                if (Selected()?.Committed <= 0) throw new InvalidOperationException("Desktop Pause did not save progress.");
                Capture(window, Path.Combine(output, "paused.png"));
                Control<Button>("ResumeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => Selected()?.State == DownloadState.Completed);
                using (var input = File.OpenRead(destination.Text))
                    if (Convert.ToHexStringLower(SHA256.HashData(input)) != Payload.ExpectedHash(8388608))
                        throw new InvalidOperationException("Desktop Resume produced different bytes.");
                destination.Text = Path.Combine(temporary, "cancelled.bin");
                download.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => status.Text == "Downloading…");
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => Selected()?.State == DownloadState.Cancelled);
                if (!status.Text.StartsWith("Cancelled.") || File.Exists(destination.Text))
                    throw new InvalidOperationException("Desktop cancellation did not finish safely.");
                Capture(window, Path.Combine(output, "cancelled.png"));
                destination.Text = Path.Combine(temporary, "closed.bin");
                download.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => Selected()?.Received > 131072);
                var interruptedId = Selected()!.Id;
                window.Close();
                await WaitUntil(() => !window.IsVisible);
                if (File.Exists(destination.Text)) throw new InvalidOperationException("Window close published a cancelled transfer.");
                foreach (var part in Directory.EnumerateFiles(temporary, "*.syphon-part"))
                    using (File.Open(part, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                window = new MainWindow(store, pipeName, htmlUi: false) { Left = -10000, Top = -10000, ShowInTaskbar = false };
                window.Show();
                await WaitUntil(() => window.IsReady && Control<DataGrid>("TaskList").Items.Count == 5);
                var restored = Control<DataGrid>("TaskList").Items.Cast<DownloadRow>().Single(t => t.Snapshot.Id == interruptedId);
                if (restored.State != DownloadState.Paused || restored.Snapshot.Committed == 0)
                    throw new InvalidOperationException("Desktop reopening did not restore the paused task.");
                Control<DataGrid>("TaskList").SelectedItem = restored;
                await Task.Delay(100);
                Capture(window, Path.Combine(output, "restored.png"));
                Control<Button>("ResumeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => Selected()?.State == DownloadState.Completed);
                var completedDestination = Selected()!.Destination;
                var countBeforeRemoval = Control<DataGrid>("TaskList").Items.Count;
                _ = window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var dialog = app.Windows.OfType<RemoveDownloadDialog>().Single();
                    ((Button)dialog.FindName("ConfirmRemoval")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                Control<Button>("RemoveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => Control<DataGrid>("TaskList").Items.Count == countBeforeRemoval - 1);
                if (!File.Exists(completedDestination)) throw new InvalidOperationException("Removing a completed row deleted the file.");

                Control<TextBox>("UrlBox").Text = new Uri(fixture.BaseAddress, "retry?key=desktop-retry&mode=503&failures=1&retry=2&size=100003").AbsoluteUri;
                Control<TextBox>("DestinationBox").Text = Path.Combine(temporary, "retry.bin");
                Control<Button>("DownloadButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => Selected()?.State == DownloadState.RetryWaiting);
                if (!Control<Button>("PauseButton").IsEnabled || !Control<TextBlock>("StatusText").Text.Contains("Retrying in"))
                    throw new InvalidOperationException("Desktop retry countdown or Pause unavailable.");
                Capture(window, Path.Combine(output, "retrying.png"));
                Control<Button>("PauseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => Selected()?.State == DownloadState.Paused);
                Control<Button>("ResumeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => Selected()?.State == DownloadState.Completed);
                Control<CheckBox>("SingleConnectionChoice").IsChecked = true;
                Control<TextBox>("UrlBox").Text = new Uri(fixture.BaseAddress, "segments?size=16777216&key=desktop-single").AbsoluteUri;
                Control<TextBox>("DestinationBox").Text = Path.Combine(temporary, "single.bin");
                Control<Button>("DownloadButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => Selected()?.State == DownloadState.Completed);
                if (Selected()?.Segments.Length != 0 || Selected()?.SingleConnection != true) throw new InvalidOperationException("Single connection choice was ignored.");
                Control<CheckBox>("SingleConnectionChoice").IsChecked = false;
                Control<TextBox>("UrlBox").Text = new Uri(fixture.BaseAddress, "segments?size=33554432&key=desktop-multi").AbsoluteUri;
                Control<TextBox>("DestinationBox").Text = Path.Combine(temporary, "multi.bin");
                Control<Button>("DownloadButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => Selected()?.Segments.Length == 4 && Selected()?.Received > 131072);
                Capture(window, Path.Combine(output, "segmented.png"));
                Control<Button>("PauseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => Selected()?.State == DownloadState.Paused);
                Control<Button>("ResumeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => Selected()?.State == DownloadState.Completed);
                using (var input = File.OpenRead(Selected()!.Destination))
                    if (Convert.ToHexStringLower(SHA256.HashData(input)) != Payload.ExpectedHash(33554432))
                        throw new InvalidOperationException("Desktop segmented bytes differ.");

                var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
                var hostBuild = Path.GetFullPath(args.ElementAtOrDefault(1) ?? $"src/Syphon.NativeHost/bin/{configuration}/net10.0-windows");
                var host = Path.Combine(temporary, "native-host"); Directory.CreateDirectory(host);
                foreach (var source in Directory.GetFiles(hostBuild)) File.Copy(source, Path.Combine(host, Path.GetFileName(source)));
                const string caller = "chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/";
                await File.WriteAllTextAsync(Path.Combine(host, "host-settings.json"), JsonSerializer.Serialize(new
                { desktopPath = Environment.ProcessPath!, allowedOrigins = new[] { caller }, pipeName }, BrowserProtocol.JsonOptions));
                var request = new BrowserRequest(1, "download", Guid.NewGuid(), new Uri(fixture.BaseAddress, "file?size=100003").AbsoluteUri);
                var rowsBeforeHandoff = Control<DataGrid>("TaskList").Items.Count;
                for (var repeat = 0; repeat < 3; repeat++)
                {
                    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    start.ArgumentList.Add(Path.Combine(host, "Syphon.NativeHost.dll")); start.ArgumentList.Add(caller);
                    using var process = Process.Start(start)!;
                    await BrowserProtocol.WriteAsync(process.StandardInput.BaseStream, request); process.StandardInput.Close();
                    var reply = await BrowserProtocol.ReadAsync<BrowserReply>(process.StandardOutput.BaseStream);
                    await process.WaitForExitAsync();
                    if (reply?.Status != "accepted") throw new InvalidOperationException("Native host did not reach the WPF coordinator.");
                }
                await WaitUntil(() => Selected()?.BrowserRequestId == request.RequestId && Selected()?.State == DownloadState.Completed);
                if (Control<DataGrid>("TaskList").Items.Count != rowsBeforeHandoff + 1)
                    throw new InvalidOperationException("Native retry duplicated a desktop row.");
                using (var input = File.OpenRead(Selected()!.Destination))
                    if (Convert.ToHexStringLower(SHA256.HashData(input)) != Payload.ExpectedHash(100003)) throw new InvalidOperationException("Native handoff download bytes differ.");
                Capture(window, Path.Combine(output, "browser-handoff.png"));

                for (var index = 0; index < 3; index++)
                {
                    Control<TextBox>("UrlBox").Text = new Uri(fixture.BaseAddress, "resume?size=8388608").AbsoluteUri;
                    Control<TextBox>("DestinationBox").Text = Path.Combine(temporary, $"queued-{index}.bin");
                    Control<Button>("DownloadButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                if (Selected()?.State != DownloadState.Queued) throw new InvalidOperationException("Desktop third transfer did not queue.");
                await Task.Delay(250);
                Capture(window, Path.Combine(output, "queued.png"));
                Control<Button>("PauseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => Selected()?.State == DownloadState.Paused);
                window.Close();
                await WaitUntil(() => !window.IsVisible);
                Console.WriteLine("PASS Desktop: prior workflows, accessibility basics, single/four-part transfers, and real native-host to WPF handoff with duplicate protection and verified bytes.");
                exitCode = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally
            {
                await fixture.DisposeAsync();
                // All generated files are confined to this unique test-owned directory.
                Directory.Delete(temporary, recursive: true);
                if (window.IsVisible) window.Close();
                app.Shutdown(exitCode);
            }
        };
        app.Run(window);
        return exitCode;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("Desktop operation did not finish.");
            await Task.Delay(30);
        }
    }

    private static void Capture(Window window, string file)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var width = (int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right);
        var height = (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            drawing.DrawRectangle(new VisualBrush(content), null,
                new Rect(content.Margin.Left, content.Margin.Top, content.ActualWidth, content.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
