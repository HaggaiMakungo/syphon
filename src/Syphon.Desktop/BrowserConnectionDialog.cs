using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Syphon.Desktop;
internal sealed class BrowserConnectionDialog : Window
{
    public BrowserConnectionDialog(Window owner)
    {
        Owner=owner;Title="Connect your browser";Width=560*LocalHtmlSurface.InterfaceScale;Height=Math.Min(720*LocalHtmlSurface.InterfaceScale,SystemParameters.WorkArea.Height-40);WindowStartupLocation=WindowStartupLocation.CenterOwner;CustomWindowFrame.Install(this,false);
        var folder=Path.Combine(AppContext.BaseDirectory,"extensions");
        var installed=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../extensions"));
        if(!Directory.Exists(folder)&&File.Exists(Path.Combine(installed,"manifest.json")))folder=installed;
        if(!File.Exists(Path.Combine(folder,"manifest.json"))){
            // An unpacked build may live under artifacts/ or the normal bin/ tree.
            // Resolve the actual source extension instead of assuming a fixed depth.
            DirectoryInfo? directory=new DirectoryInfo(AppContext.BaseDirectory);
            for(var depth=0;directory is not null&&depth<6;depth++,directory=directory.Parent){
                var source=Path.Combine(directory.FullName,"extensions","syphon");
                if(File.Exists(Path.Combine(source,"manifest.json"))){folder=source;break;}
            }
        }
        string? error=null;LocalHtmlSurface? ui=null;Content=new Grid();
        ui=new LocalHtmlSurface(this,(FrameworkElement)Content,()=>new {kind="connection",extensionFolder=folder,error},message=>{
            try{
                var action=message.GetProperty("action").GetString();
                if(action=="close")Close();
                else if(action=="layout"){var height=message.GetProperty("height").GetDouble();if(double.IsFinite(height)&&height is >=200 and <=1600)Height=Math.Min(height*LocalHtmlSurface.InterfaceScale,SystemParameters.WorkArea.Height-40);}
                else if(action=="copy-browser-address")Clipboard.SetText(message.GetProperty("browser").GetString()=="edge"?"edge://extensions":"chrome://extensions");
                else if(action=="copy-extension-folder")Clipboard.SetText(folder);
                else if(action=="extension-folder"){
                    if(!Directory.Exists(folder))throw new IOException("The extension folder was not found. Use Syphon Setup to install browser integration.");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder){UseShellExecute=true});
                }
            }catch(Exception ex) when(ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception){error=ex.Message;ui?.Publish();}
            return Task.CompletedTask;
        });
    }
}
