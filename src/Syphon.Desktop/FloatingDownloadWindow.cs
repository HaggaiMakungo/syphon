using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Syphon.Core;
using Syphon.Engine;
namespace Syphon.Desktop;

public sealed partial class FloatingDownloadWindow : Window
{
    private readonly Func<Guid, Task> _pauseTask;
    private readonly Action<Guid> _resumeTask;
    private readonly Func<Guid, Task> _cancelTask;
    private StoredDownload? current;
    private DateTimeOffset lastSample;
    private readonly RechartsSpeedChart chart = new() { Height=72 };
    private readonly Queue<double> speeds = new();
    private readonly Queue<DateTimeOffset> speedTimes = new();
    private readonly StackPanel sharedRows = new();
    private readonly ScrollViewer sharedScroll;
    private readonly TextBlock nativeTitle;
    private bool expanded;
    public Guid? TaskId => current?.Id;
    public FloatingDownloadWindow(Func<Guid,Task> pauseTask, Action<Guid> resumeTask, Func<Guid,Task> cancelTask,
        Action openManager, Action<Guid> dismissed, bool htmlUi=true, string? geometryPath=null)
    {
        _pauseTask=pauseTask; _resumeTask=resumeTask; _cancelTask=cancelTask;
        Title="Syphon — Downloads"; Width=480; Height=660; MinWidth=360; MinHeight=240;
        WindowStyle=WindowStyle.None; AllowsTransparency=false; ShowInTaskbar=false; ShowActivated=false;
        Background=new SolidColorBrush(Color.FromRgb(0x18,0x12,0x21));
        CustomWindowFrame.Install(this,true);
        NameScope.SetNameScope(this,new NameScope());
        var root=new DockPanel {Margin=new Thickness(8)};
        var header=new DockPanel {Height=44};
        void Dismiss(){foreach(var t in _htmlTasks) dismissed(t.Id);Hide();}
        var close=new Button {Content="×",Width=36,ToolTip="Close window — downloads continue"};
        close.Name="FloatingClose"; RegisterName(close.Name,close);
        close.Click+=(_,_)=>Dismiss(); DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var mode=new Button {Content="Expand",Padding=new Thickness(8,4,8,4),Margin=new Thickness(4)};
        mode.Click+=(_,_)=>{expanded=!expanded;mode.Content=expanded?"Compact":"Expand";sharedScroll!.MaxHeight=expanded?double.PositiveInfinity:448;UpdateRows();};
        DockPanel.SetDock(mode,Dock.Right);header.Children.Add(mode);
        nativeTitle=new TextBlock {Text=Title,Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,0,0)};
        header.Children.Add(nativeTitle);header.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource is TextBlock && e.LeftButton==MouseButtonState.Pressed)DragMove();};
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var actions=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(4)};
        Button ActionButton(string label,Action action){var b=new Button {Content=label,Padding=new Thickness(10,7,10,7),Margin=new Thickness(3)};b.Click+=(_,_)=>action();actions.Children.Add(b);return b;}
        ActionButton("Pause all",async()=>await RunAll(false)); ActionButton("Resume all",async()=>await RunAll(true));ActionButton("Manager",openManager);
        DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        sharedScroll=new ScrollViewer {Content=sharedRows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=448,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var nativeBody = new DockPanel(); DockPanel.SetDock(chart,Dock.Bottom); nativeBody.Children.Add(chart);nativeBody.Children.Add(sharedScroll);root.Children.Add(nativeBody);Content=root;
        RegisterName("FloatingSpeedChart",chart);Closed+=(_,_)=>chart.Dispose();
        RegisterName("SharedDownloadRows",sharedRows);RegisterName("SharedDownloadScroll",sharedScroll);
        Left=SystemParameters.WorkArea.Right-Width-24;Top=SystemParameters.WorkArea.Bottom-Height-24;
        Closing+=(_,_)=>{if(IsVisible)foreach(var t in _htmlTasks)dismissed(t.Id);};
        if(htmlUi)InstallHtml(openManager,dismissed);
        if(geometryPath is not null)WindowGeometryStore.Attach(this,geometryPath);
    }
    public void Update(StoredDownload task,StoredDownload[] tasks)
    {
        current=task;
        _htmlTasks=tasks.Where(t=>t.State!=DownloadState.Completed && t.State!=DownloadState.Cancelled || t.Id==task.Id).ToArray();
        var count=_htmlTasks.Count(t=>t.State is DownloadState.Downloading or DownloadState.Starting or DownloadState.RetryWaiting or DownloadState.Assembling or DownloadState.Publishing);
        Title=$"Syphon — {count} active downloads";nativeTitle.Text=Title;
        if(DateTimeOffset.UtcNow-lastSample>=TimeSpan.FromMilliseconds(500)){lastSample=DateTimeOffset.UtcNow;speeds.Enqueue(_htmlTasks.Sum(t=>t.State==DownloadState.Downloading?Math.Max(0,t.BytesPerSecond):0));speedTimes.Enqueue(lastSample);
        while(speeds.Count>120){speeds.Dequeue();speedTimes.Dequeue();}chart.Update(speeds);}
        UpdateRows();PublishHtml();
    }
    private void UpdateRows()
    {
        if(FindName("FloatingPause") is not null)UnregisterName("FloatingPause");
        if(FindName("FloatingCancel") is not null)UnregisterName("FloatingCancel");
        sharedRows.Children.Clear();
        foreach(var task in _htmlTasks){
            var row=new DownloadRow(task);var panel=new StackPanel {Margin=new Thickness(10),MinHeight=92};
            panel.Children.Add(new TextBlock {Text=task.Filename,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=task.Destination,Foreground=Brushes.White});
            panel.Children.Add(new ProgressBar {Maximum=100,Height=5,Margin=new Thickness(0,8,0,6),Value=row.ProgressPercent,IsIndeterminate=row.UnknownProgress});
            panel.Children.Add(new TextBlock {Text=$"{(task.Total>0?row.ProgressPercent.ToString("0.#")+"%":"Size unknown")} · {DownloadDisplay.Speed(task)} · {DownloadDisplay.Status(task)}",Foreground=Brushes.LightGray});
            if(expanded)panel.Children.Add(new TextBlock {Text=$"{DownloadDisplay.Progress(task)} · ETA {DownloadDisplay.Remaining(task)} · {task.Connections} connections",Foreground=Brushes.LightGray});
            var buttons=new StackPanel {Orientation=Orientation.Horizontal};
            var toggle=new Button {Content=row.CanResume?"Resume":"Pause",IsEnabled=row.CanResume||row.CanPause,Margin=new Thickness(0,4,6,0)};
            toggle.Click+=async(_,_)=>{try{if(row.CanResume)_resumeTask(task.Id);else await _pauseTask(task.Id);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Download action");}};
            var cancel=new Button {Content="Cancel",IsEnabled=row.CanPause,Margin=new Thickness(0,4,0,0)};cancel.Click+=async(_,_)=>{try{await _cancelTask(task.Id);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Download action");}};
            if(task.Id==current?.Id){toggle.Name="FloatingPause";cancel.Name="FloatingCancel";RegisterName(toggle.Name,toggle);RegisterName(cancel.Name,cancel);}
            buttons.Children.Add(toggle);buttons.Children.Add(cancel);panel.Children.Add(buttons);sharedRows.Children.Add(panel);
        }
    }
    private async Task RunAll(bool resume)
    {
        foreach(var task in _htmlTasks.ToArray()){
            var row=new DownloadRow(task);
            if(resume&&row.CanResume)_resumeTask(task.Id);else if(!resume&&row.CanPause)await _pauseTask(task.Id);
        }
    }
}
