using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Syphon.Desktop;

internal sealed class RestartDownloadDialog : Window
{
    public RestartDownloadDialog(Window owner,string filename)
    {
        Owner=owner;Title="Restart download";Width=510;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var body=new StackPanel { Margin=new Thickness(27) };
        body.Children.Add(new TextBlock { Text="Start from the beginning?",FontSize=23,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text="This download’s unfinished data and saved progress will be discarded. Existing completed files will stay where you saved them.",Foreground=(Brush)FindResource("Sub"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,15,0,25),LineHeight=21 });
        var actions=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right };
        var cancel=new Button { Content="Keep download",IsCancel=true };var restart=new Button { Content="Restart download",Background=(Brush)FindResource("Mid"),Margin=new Thickness(10,0,0,0) };restart.Click+=(_,_)=>DialogResult=true;
        actions.Children.Add(cancel);actions.Children.Add(restart);body.Children.Add(actions);Content=CustomWindowFrame.WrapNativeDialog(this,body);
        Width=514*LocalHtmlSurface.InterfaceScale;Height=Math.Min(620*LocalHtmlSurface.InterfaceScale,SystemParameters.WorkArea.Height-40);SizeToContent=SizeToContent.Manual;
        _=new LocalHtmlSurface(this,(FrameworkElement)Content,()=>new {kind="confirmation",confirmation="restart",filename},message=>{
            var action=message.GetProperty("action").GetString();
            if(action=="confirm")DialogResult=true;
            else if(action=="close")Close();
            else if(action=="layout"){var height=message.GetProperty("height").GetDouble();if(double.IsFinite(height)&&height is >=200 and <=1600)Height=Math.Min(height*LocalHtmlSurface.InterfaceScale,SystemParameters.WorkArea.Height-40);}
            return Task.CompletedTask;
        });
    }
}
