using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
namespace Syphon.Desktop;
internal sealed class TrayPopupWindow : Window
{
    private readonly LocalHtmlSurface surface;
    private bool closing;
    internal TrayPopupWindow(Action<string> command,Func<object> model)
    {
        Title="Syphon";Width=300*LocalHtmlSurface.InterfaceScale;Height=270*LocalHtmlSurface.InterfaceScale;ShowInTaskbar=false;Topmost=true;ResizeMode=ResizeMode.NoResize;CustomWindowFrame.Install(this,false);
        Left=SystemParameters.WorkArea.Right-Width-12;Top=SystemParameters.WorkArea.Bottom-Height-12;Content=new Grid();
        surface=new LocalHtmlSurface(this,(FrameworkElement)Content,model,message=>{
            var action=message.GetProperty("action").GetString();
            if(action=="close")Close();
            else if(action=="layout"){var height=message.GetProperty("height").GetDouble();if(double.IsFinite(height)&&height is >=60 and <=800){Height=height*LocalHtmlSurface.InterfaceScale;Top=SystemParameters.WorkArea.Bottom-Height-12;}}
            else if(action=="tray-command"){
                var name=message.GetProperty("command").GetString();if(name is "open" or "pause" or "resume" or "exit"){Close();command(name);}
            }
            return Task.CompletedTask;
        });
        Deactivated+=(_,_)=>{if(!closing)Close();};
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing an active native window can synchronously deactivate it.
        // Mark the transition before raising Closing so focus loss cannot re-enter Close.
        closing=true;
        base.OnClosing(e);
        if(e.Cancel)closing=false;
    }
    internal void Refresh()=>surface.Publish();
}
