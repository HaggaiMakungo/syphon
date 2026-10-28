using System.Text.Json;
using Syphon.Engine;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace Syphon.Desktop;

public sealed partial class DownloadOptionsDialog
{
    public LocalHtmlSurface? HtmlSurface { get; private set; }
    private string? htmlFolder;
    private int pasteVersion;
    private CancellationTokenSource? previewCancellation;
    private string? previewUrl, previewDetails,previewFilename;
    private bool previewClosed;
    private async Task PreviewSourceAsync(string value)
    {
        previewCancellation?.Cancel();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(url.UserInfo)) return;
        using var cancellation = new CancellationTokenSource(); previewCancellation = cancellation; previewUrl = value;
        try {
            var preview = MediaFormat is null?await DownloadPreviewReader.ReadAsync(url,cancellation.Token):new DownloadPreview(DownloadCoordinator.SuggestedBrowserFilename(url,MediaFormat),null,"Video details and final size are resolved when downloading.");
            if (previewClosed || cancellation.IsCancellationRequested || previewCancellation != cancellation) return;
            previewFilename=DownloadCoordinator.SuggestedBrowserFilename(url,MediaFormat);
            previewDetails = (preview.Bytes is long bytes ? FormatSize(bytes) : "Size unavailable") + " · " + preview.Message;
            HtmlSurface?.Publish();
        } catch (Exception ex) when (ex is OperationCanceledException or ArgumentException or Syphon.Core.DownloadException) { }
        finally { if (previewCancellation == cancellation) previewCancellation = null; }
    }
    private void InstallHtml(string title, string? quality, long? estimatedBytes, bool editable)
    {
        Width = 2 + 500 * LocalHtmlSurface.InterfaceScale; Height = Math.Min(720, SystemParameters.WorkArea.Height - 60); SizeToContent = SizeToContent.Manual;
        CustomWindowFrame.Install(this, resizable: true);
        HtmlSurface = new LocalHtmlSurface(this, (FrameworkElement)Content, () => new {
            kind = "dialog", pasteVersion, error = error.Text, folder = htmlFolder, previewUrl, previewDetails,previewFilename,
            form = new { mediaFormat=MediaFormat??"auto",url = source.Text, filename = filename.Text, folder = folder.Text, single = single.IsChecked == true,
                remember = remember.IsChecked == true, priority = Priority, editable, details = (quality ?? "Original file") + " · " + FormatSize(estimatedBytes) }
        }, async message => {
            var action = message.GetProperty("action").GetString();
            if (CustomWindowFrame.Handle(this, action)) return;
            if (action == "layout") { var height = message.GetProperty("height").GetDouble(); if (double.IsFinite(height) && height is >= 200 and <= 1600) Height = Math.Min(SystemParameters.WorkArea.Height - 40, height * LocalHtmlSurface.InterfaceScale); return; }
            if (action == "close") { Close(); return; }
            if (action == "drag") { if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove(); return; }
            if (action == "preview-dialog") { if (editable) {if(message.TryGetProperty("mediaFormat",out var output)){var value=output.GetString();if(value is not ("auto" or "mp4" or "mp3"))throw new ArgumentException("Choose Automatic, Video or Audio.");MediaFormat=value=="auto"?null:value;}_ = PreviewSourceAsync(message.GetProperty("url").GetString() ?? "");}return; }
            if (action == "paste-dialog") { if (editable && Clipboard.ContainsText()) { source.Text = Clipboard.GetText(); pasteVersion++; HtmlSurface?.Publish(); } return; }
            if (action is not ("submit" or "browse-dialog")) return;
            var form = message.GetProperty("form");
            if (editable) source.Text = form.GetProperty("url").GetString() ?? "";
            FilenameEdited=form.TryGetProperty("preserveFilename",out var preserveName)&&preserveName.GetBoolean();
            filename.Text = form.GetProperty("filename").GetString() ?? "";
            folder.Text = form.GetProperty("folder").GetString() ?? "";
            single.IsChecked = form.GetProperty("single").GetBoolean();
            remember.IsChecked = form.GetProperty("remember").GetBoolean();
            Priority = form.TryGetProperty("priority", out var priority) ? priority.GetInt32() : 0;
            if (Priority is < -1 or > 1) throw new ArgumentException("Choose Low, Normal or High priority.");
            if(editable&&form.TryGetProperty("mediaFormat",out var selectedOutput)){
                var output=selectedOutput.GetString();if(output is not ("auto" or "mp4" or "mp3"))throw new ArgumentException("Choose Automatic, Video or Audio.");MediaFormat=output=="auto"?null:output;
                if(MediaFormat is not null)filename.Text=System.IO.Path.ChangeExtension(filename.Text,"."+MediaFormat);
            }
            if (action == "submit") { htmlFolder = null; Submit(form.GetProperty("immediately").GetBoolean()); }
            else { var picker = new OpenFolderDialog { Title = "Choose a download folder" }; if (picker.ShowDialog(this) == true) { folder.Text = picker.FolderName; htmlFolder = folder.Text; } HtmlSurface?.Publish(); }
            await Task.CompletedTask;
        });
        Loaded += (_, _) => { if (estimatedBytes is null && !string.IsNullOrWhiteSpace(source.Text)) _ = PreviewSourceAsync(source.Text); };
        Closed += (_, _) => { previewClosed = true; previewCancellation?.Cancel(); };
    }
}
