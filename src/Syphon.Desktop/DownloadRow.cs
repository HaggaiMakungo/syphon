using System.ComponentModel;
using Syphon.Core;
using Syphon.Engine;

namespace Syphon.Desktop;

public sealed class DownloadRow(StoredDownload snapshot) : INotifyPropertyChanged
{
    public StoredDownload Snapshot { get; private set; } = snapshot;
    public string Filename => Snapshot.Filename;
    public string Host => Uri.TryCreate(Snapshot.Url, UriKind.Absolute, out var url) ? url.Host : "";
    public string SizeText => Snapshot.State == DownloadState.Completed ? DownloadDisplay.Bytes(Snapshot.Received) : DownloadDisplay.ExpectedSize(Snapshot);
    public string DigestText => Snapshot.Sha256 is null ? "—" : "Recorded";
    public string FileType => DownloadCategories.Category(Filename,Snapshot.MediaFormat) switch {
        "Videos"=>"Video", "Music"=>"Audio", "Compressed"=>"Archives", "Misc"=>System.IO.Path.GetExtension(Filename).ToLowerInvariant() is ".iso" or ".xip"?"Archives":"Other", var category=>category };
    public DownloadState State => Snapshot.State;
    public string ProgressText => DownloadDisplay.Progress(Snapshot);
    public string StatusText => DownloadDisplay.Status(Snapshot);
    public string SpeedText => DownloadDisplay.Speed(Snapshot);
    public string RemainingText => DownloadDisplay.Remaining(Snapshot);
    public double ProgressPercent => Snapshot.Total is > 0 ? Math.Clamp(Snapshot.Received * 100.0 / Snapshot.Total.Value, 0, 100) : 0;
    public bool UnknownProgress => Snapshot.Total is null && State is DownloadState.Downloading or DownloadState.Starting;
    public bool CanResume => !Snapshot.RestartRequired && State is DownloadState.Paused or DownloadState.Cancelled or DownloadState.Failed;
    public bool CanPause => State is DownloadState.Downloading or DownloadState.Starting or DownloadState.Queued or DownloadState.RetryWaiting or DownloadState.Assembling or DownloadState.Publishing;
    public bool CanAct => CanResume || CanPause;
    public string PrimaryAction => CanResume ? "Resume" : "Pause";
    public System.Windows.Media.Brush StatusBrush => (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource(State switch {
        DownloadState.Paused => "Warning", DownloadState.Failed or DownloadState.Cancelled => "Danger",
        DownloadState.Downloading when Snapshot.Connections > 1 => "Accent",
        DownloadState.Downloading or DownloadState.Completed => "Success", _ => "Muted" });
    public string ConnectionSummary => Snapshot.Connections > 1 ? $"{Host} · {Snapshot.Connections} connections" : Host;
    public string TransferMode => Snapshot.Connections > 1 ? "Segmented" : StatusText;
    public string ConnectionsText => Snapshot.State == DownloadState.Downloading ? Snapshot.Connections.ToString()
        : Snapshot.Segments.Length > 0 ? $"{Snapshot.Segments.Length} parts" : "—";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(StoredDownload value)
    {
        if (ReferenceEquals(Snapshot, value)) return;
        Snapshot = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
