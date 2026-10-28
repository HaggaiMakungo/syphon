using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Syphon.Desktop;

public sealed class UpdateDownloadWindow : Window
{
    private readonly TextBlock status;
    private readonly ProgressBar progress;
    private readonly Button update, cancel;
    private readonly Func<CancellationToken,Task> install;
    private CancellationTokenSource? cancellation;
    private bool busy, installing;
    public UpdateDownloadWindow(string tag,string notes,bool supported,Func<CancellationToken,Task> updateNow,Action openRelease,Action later,Action skip)
    {
        install=updateNow;Title="Syphon update";Width=600;Height=610;MinWidth=420;MinHeight=410;
        var body=new StackPanel {Margin=new Thickness(26)};
        body.Children.Add(new TextBlock {Text="Syphon "+tag+" is available",FontSize=22,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18)});
        body.Children.Add(new TextBox {Text=notes,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=210,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        body.Children.Add(new TextBlock {Text=supported?"Syphon downloads and verifies the installer in the background. Installing closes Syphon and Windows may ask for administrator approval.":"This release has no supported verified Windows installer. Open the release page to review its downloads.",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)Application.Current.FindResource("Sub"),Margin=new Thickness(0,16,0,12)});
        status=new TextBlock {Name="UpdateStatus",Text=supported?"Ready to download the update.":"In-app installation unavailable for this release.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};body.Children.Add(status);
        progress=new ProgressBar {Name="UpdateProgress",Maximum=100,Height=6,Margin=new Thickness(0,0,0,12),Visibility=Visibility.Collapsed};body.Children.Add(progress);
        update=new Button {Name="UpdateNow",Content="Update now",IsEnabled=supported,Margin=new Thickness(0,0,0,10),Style=(Style)Application.Current.FindResource("PrimaryButton"),Background=new LinearGradientBrush(Color.FromRgb(0x74,0x4b,0xe0),Color.FromRgb(0x4b,0x24,0xac),90)};
        update.Click+=async(_,_)=>await StartAsync();body.Children.Add(update);
        cancel=new Button {Name="CancelUpdate",Content="Cancel download",Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,10)};cancel.Click+=(_,_)=>{cancellation?.Cancel();status.Text="Cancelling update download…";cancel.IsEnabled=false;};body.Children.Add(cancel);
        var links=new WrapPanel();
        void Link(string label,Action action){var b=new Button {Content=label,Padding=new Thickness(10,8,10,8),Margin=new Thickness(0,0,6,8)};b.Click+=(_,_)=>{if(busy&&label!="Open release page")return;action();if(label!="Open release page")Close();};links.Children.Add(b);}
        Link("Open release page",openRelease);Link("Remind me later",later);Link("Skip this version",skip);body.Children.Add(links);
        DialogAppearance.Apply(this,new ScrollViewer {Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},true);
        Closing+=(_,e)=>{if(installing){e.Cancel=true;return;}cancellation?.Cancel();};
        NameScope.SetNameScope(this,new NameScope());RegisterName(status.Name,status);RegisterName(progress.Name,progress);RegisterName(update.Name,update);RegisterName(cancel.Name,cancel);
    }
    private async Task StartAsync()
    {
        if(busy)return;busy=true;cancellation=new CancellationTokenSource();update.IsEnabled=false;cancel.IsEnabled=true;cancel.Visibility=Visibility.Visible;progress.Visibility=Visibility.Visible;progress.IsIndeterminate=true;status.Text="Downloading update…";
        try{await install(cancellation.Token);}
        catch(OperationCanceledException ex){installing=false;status.Text=cancellation.IsCancellationRequested?"Update download cancelled. Your other downloads continue.":ex.Message;update.Content="Retry download";}
        catch(Exception ex){ShowFailure(ex.Message);}
        finally{busy=false;cancellation.Dispose();cancellation=null;update.IsEnabled=true;cancel.Visibility=Visibility.Collapsed;progress.IsIndeterminate=false;}
    }
    public void AllowShutdown(){installing=false;}
    public void CompleteHandoff(){installing=false;Close();}
    public void ReportProgress(long received,long? total)
    {
        progress.Visibility=Visibility.Visible;progress.IsIndeterminate=total is not >0;progress.Value=total is >0?Math.Clamp(100.0*received/total.Value,0,100):0;
        status.Text=total is >0?$"Downloading update · {received/1048576.0:0.#} of {total.Value/1048576.0:0.#} MB · {progress.Value:0}%":$"Downloading update · {received/1048576.0:0.#} MB";
    }
    public void ShowReady(string message="Update verified. Preparing installation…"){installing=true;progress.IsIndeterminate=false;progress.Value=100;status.Text=message;cancel.IsEnabled=false;}
    public void ShowFailure(string message){installing=false;status.Text="Update could not finish. "+message;update.Content="Retry update";}
}
