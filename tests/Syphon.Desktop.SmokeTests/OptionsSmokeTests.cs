using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Syphon.BrowserBridge;
using Syphon.Core;
using Syphon.Desktop;
using Syphon.TestServer;

namespace Syphon.Desktop.SmokeTests;

internal static class OptionsSmokeTests
{
    public static int Run(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/download-options"); Directory.CreateDirectory(output);
        var root = Path.Combine(Path.GetTempPath(), "Syphon.Options.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var pipe = "Syphon.Options." + Guid.NewGuid().ToString("N");
        var window = new MainWindow(root, pipe, htmlUi: false) { Left = -10000, Top = -10000, ShowInTaskbar = false };
        int code = 1;
        window.Loaded += async (_, _) => {
            await using var fixture = await FixtureServer.StartAsync();
            try {
                T Control<T>(DownloadOptionsDialog dialog, string name) => (T)dialog.FindName(name);
                DownloadOptionsDialog? Dialog() => app.Windows.OfType<DownloadOptionsDialog>().SingleOrDefault();
                DataGrid Rows() => (DataGrid)window.FindName("TaskList");
                Task<BrowserReply> Send(BrowserRequest request) => BrowserPipeClient.SendAsync(pipe, request, TimeSpan.FromSeconds(60));
                await Until(() => window.IsReady);
                var managerChart = (RechartsSpeedChart)window.FindName("ManagerSpeedChart");
                await Until(() => managerChart.IsChartReady || managerChart.InitializationError is not null);
                Check(managerChart.IsChartReady, "Manager Recharts failed: " + managerChart.InitializationError);
                var request = new BrowserRequest(1, "download", Guid.NewGuid(), new Uri(fixture.BaseAddress, "file?size=100003").AbsoluteUri,
                    Title: "An example video", ShowOptions: true, Quality: "1080p", EstimatedBytes: 125000000);
                var queued = Send(request); await Until(() => Dialog() is not null);
                var dialog = Dialog()!;
                Check(dialog.Topmost, "New Download must stay above other windows.");
                Check(Rows().Items.Count == 0 && !queued.IsCompleted, "A download started before confirmation.");
                Check(Control<TextBlock>(dialog, "DownloadDetails").Text == "1080p  ·  About 125 MB", "Quality or size detail was lost.");
                var duplicate = Send(request); await Task.Delay(100); Check(app.Windows.OfType<DownloadOptionsDialog>().Count() == 1, "Duplicate handoff opened another window.");
                var conflict = await Send(request with { Url = request.Url + "&different=1" }); Check(conflict.Status == "rejected", "Pending request conflict was accepted.");
                Control<TextBox>(dialog, "DownloadFilename").Text = "../escape.bin";
                Control<Button>(dialog, "ConfirmDownload").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!string.IsNullOrEmpty(Control<TextBlock>(dialog, "DownloadOptionsError").Text) && Rows().Items.Count == 0, "Invalid filename created a task.");
                var chosenFolder = Path.Combine(root, "Chosen downloads");
                Control<TextBox>(dialog, "DownloadFilename").Text = "My edited filename.bin";
                Control<TextBox>(dialog, "DownloadFolder").Text = chosenFolder;
                Control<CheckBox>(dialog, "RememberDownloadFolder").IsChecked = true;
                dialog.ShowError(""); Capture(dialog, Path.Combine(output, "download-options.png"));
                Control<Button>(dialog, "QueueDownload").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var queuedReply = await queued; Check(queuedReply.Status == "accepted" && (await duplicate).TaskId == queuedReply.TaskId, "Queued task was not acknowledged once.");
                await Until(() => Rows().Items.Count == 1);
                var row = Rows().Items.Cast<DownloadRow>().Single();
                Check(row.Snapshot.State == DownloadState.Paused && row.Snapshot.Received == 0 && !File.Exists(row.Snapshot.PartPath), "Queued item started downloading.");
                Check(row.Snapshot.Destination == Path.Combine(chosenFolder, "My edited filename.bin"), "Edited destination was lost.");
                Rows().SelectedItem = row; ((Button)window.FindName("ResumeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => row.Snapshot.State == DownloadState.Completed);
                Check(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(row.Snapshot.Destination))) == Payload.ExpectedHash(100003), "Resumed queued bytes differ.");
                var retry = await Send(request); Check(retry.TaskId == queuedReply.TaskId && Dialog() is null && Rows().Items.Count == 1, "Confirmed retry opened another dialog or task.");
                var cancelled = Send(request with { RequestId = Guid.NewGuid() }); await Until(() => Dialog() is not null); dialog = Dialog()!;
                Check(Control<TextBox>(dialog, "DownloadFolder").Text == chosenFolder && Control<CheckBox>(dialog, "RememberDownloadFolder").IsChecked == true, "Folder preference did not reload from disk.");
                Control<Button>(dialog, "CancelDownloadOptions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check((await cancelled).Status == "cancelled" && Rows().Items.Count == 1, "Cancel created a task.");
                var immediate = Send(request with { RequestId = Guid.NewGuid() }); await Until(() => Dialog() is not null); dialog = Dialog()!;
                Control<TextBox>(dialog, "DownloadFilename").Text = "Download now.bin";
                Control<CheckBox>(dialog, "RememberDownloadFolder").IsChecked = false;
                Control<Button>(dialog, "ConfirmDownload").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var immediateReply = await immediate; Check(immediateReply.Status == "accepted", "Download now failed.");
                await Until(() => Rows().Items.Cast<DownloadRow>().Any(r => r.Snapshot.Id == immediateReply.TaskId && r.Snapshot.State == DownloadState.Completed));
                var immediateRow = Rows().Items.Cast<DownloadRow>().Single(r => r.Snapshot.Id == immediateReply.TaskId);
                Check(immediateRow.Snapshot.Destination == Path.Combine(chosenFolder, "Misc", "Download now.bin"), "Remembered download root did not use its category folder.");
                Check(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(immediateRow.Snapshot.Destination))) == Payload.ExpectedHash(100003), "Download now bytes differ.");
                var reset = Send(request with { RequestId = Guid.NewGuid() }); await Until(() => Dialog() is not null); dialog = Dialog()!;
                Check(Control<TextBox>(dialog, "DownloadFolder").Text == Path.Combine(root, "BrowserDownloads") && Control<CheckBox>(dialog, "RememberDownloadFolder").IsChecked == false, "Unchecking Remember did not clear the preference.");
                dialog.Close(); Check((await reset).Status == "cancelled", "Closing the window was not cancellation.");
                var mp3 = Send(request with { RequestId = Guid.NewGuid(), Format = "mp3", Quality = "MP3 audio", EstimatedBytes = null }); await Until(() => Dialog() is not null); dialog = Dialog()!;
                Check(Control<TextBox>(dialog, "DownloadFilename").Text.EndsWith(".mp3") && Control<TextBlock>(dialog, "DownloadDetails").Text == "MP3 audio  ·  Size unavailable", "MP3 output details differ.");
                Control<TextBox>(dialog, "DownloadFilename").Text = "wrong.mp4";
                Control<Button>(dialog, "QueueDownload").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Control<TextBlock>(dialog, "DownloadOptionsError").Text.Contains(".mp3") && Rows().Items.Count == 2, "MP3 extension validation created a wrong-format task.");
                dialog.Close(); Check((await mp3).Status == "cancelled", "MP3 cancel was not acknowledged.");
                ((Button)window.FindName("HistoryNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Rows().Items.Count == 2, "History did not include completed files.");
                ((TextBox)window.FindName("SearchBox")).Text = "My edited";
                Check(Rows().Items.Count == 1, "History search did not filter filenames.");
                ((ComboBox)window.FindName("FileTypeFilter")).SelectedIndex = 1;
                Check(Rows().Items.Count == 0, "History type filter did not apply.");
                ((ComboBox)window.FindName("FileTypeFilter")).SelectedIndex = 0;
                ((TextBox)window.FindName("SearchBox")).Text = "";
                Capture(window, Path.Combine(output, "history.png"));
                ((Button)window.FindName("SettingsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var settings = (SettingsPage)((ContentControl)window.FindName("SettingsView")).Content;
                ((TextBox)settings.FindName("SettingsFolder")).Text = chosenFolder;
                ((CheckBox)settings.FindName("SettingsSingleConnection")).IsChecked = true;
                ((CheckBox)settings.FindName("SettingsFloating")).IsChecked = false;
                ((Button)settings.FindName("SaveSettings")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(((TextBlock)settings.FindName("SettingsResult")).Text.StartsWith("Settings saved"), "Settings did not persist.");
                Capture(window, Path.Combine(output, "settings.png"));
                ((Button)window.FindName("DownloadsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((Button)window.FindName("NewDownloadButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => Dialog() is not null); dialog = Dialog()!;
                Check(dialog.Topmost, "Manual New Download must stay above other windows.");
                Check(Control<TextBox>(dialog, "DownloadFolder").Text == chosenFolder && Control<CheckBox>(dialog, "SingleDownloadConnection").IsChecked == true, "New download did not use settings defaults.");
                Control<TextBox>(dialog, "DownloadSourceUrl").Text = new Uri(fixture.BaseAddress, "resume?size=67108864").AbsoluteUri;
                Control<TextBox>(dialog, "DownloadFilename").Text = "Floating transfer.bin";
                Capture(dialog, Path.Combine(output, "new-download.png"));
                Control<Button>(dialog, "QueueDownload").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => Rows().Items.Count == 3);
                var floatingRow = Rows().Items.Cast<DownloadRow>().Single(r => r.Snapshot.Filename == "Floating transfer.bin");
                Check(floatingRow.Snapshot.SingleConnection && floatingRow.State == DownloadState.Paused, "Manual queue did not preserve the single-connection preference.");
                Rows().SelectedItem = floatingRow;
                Rows().ScrollIntoView(floatingRow); window.UpdateLayout();
                var card = (DataGridRow)Rows().ItemContainerGenerator.ContainerFromItem(floatingRow);
                var primary = Descendants(card).OfType<Button>().Single(b => b.Command == System.Windows.Input.ApplicationCommands.Open);
                Check((string)primary.Content == "Resume" && ((System.Windows.Input.RoutedCommand)primary.Command).CanExecute(primary.CommandParameter, primary), "Queued card does not expose Resume.");
                ((System.Windows.Input.RoutedCommand)primary.Command).Execute(primary.CommandParameter, primary);
                await Until(() => floatingRow.State == DownloadState.Downloading);
                await Until(() => floatingRow.Snapshot.Received > 131072);
                Rows().ScrollIntoView(floatingRow); window.UpdateLayout();
                card = (DataGridRow)Rows().ItemContainerGenerator.ContainerFromItem(floatingRow);
                primary = Descendants(card).OfType<Button>().Single(b => b.Command == System.Windows.Input.ApplicationCommands.Open);
                ((System.Windows.Input.RoutedCommand)primary.Command).Execute(primary.CommandParameter, primary);
                await Until(() => floatingRow.State == DownloadState.Paused);
                ((Button)window.FindName("FloatingWindowButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var floating = app.Windows.OfType<FloatingDownloadWindow>().Single();
                var speedChart = (RechartsSpeedChart)floating.FindName("FloatingSpeedChart");
                await Until(() => speedChart.IsChartReady || speedChart.InitializationError is not null);
                Check(speedChart.IsChartReady, "Floating Recharts failed: " + speedChart.InitializationError);
                ((Button)floating.FindName("FloatingPause")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => floatingRow.Snapshot.Received > 131072);
                ((Button)window.FindName("FloatingWindowButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(550);
                ((Button)window.FindName("FloatingWindowButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => speedChart.RenderedSampleCount >= 2);
                await speedChart.CaptureAsync(Path.Combine(output, "recharts-speed.png"));
                Check(managerChart.RenderedSampleCount >= 2, "Real speed samples did not reach manager Recharts.");
                Capture(floating, Path.Combine(output, "active-download.png"));
                Capture(window, Path.Combine(output, "dashboard.png"));
                ((Button)floating.FindName("FloatingPause")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => floatingRow.State == DownloadState.Paused);
                ((Button)window.FindName("FloatingWindowButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((Button)floating.FindName("FloatingPause")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => floatingRow.State == DownloadState.Downloading);
                ((Button)floating.FindName("FloatingClose")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!floating.IsVisible && floatingRow.State == DownloadState.Downloading, "Closing the floating window stopped the task.");
                ((Button)window.FindName("FloatingWindowButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((Button)floating.FindName("FloatingCancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => floatingRow.State == DownloadState.Cancelled);
                window.Close(); await Until(() => !window.IsVisible);
                Console.WriteLine("PASS Desktop UI: options/pipe handoff, validation, duplicate/conflict protection, persistent preferences, queue/resume, verified bytes, history filters, settings defaults, manual dialog, card resume/pause and live floating pause/resume/close/cancel.");
                code = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally {
                if (window.IsVisible) { window.Close(); await Until(() => !window.IsVisible); }
                Directory.Delete(root, true); app.Shutdown(code);
            }
        };
        app.Run(window); return code;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent) {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> condition) {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!condition()) { if (DateTime.UtcNow > until) throw new TimeoutException("Download options operation timed out."); await Task.Delay(25); }
    }
    private static void Capture(Window window, string path) {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content;
        var width = (int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right);
        var height = (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom);
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) {
            drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            drawing.DrawRectangle(new VisualBrush(content), null, new Rect(content.Margin.Left, content.Margin.Top, content.ActualWidth, content.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
