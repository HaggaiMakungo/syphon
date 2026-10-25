using System.Windows;
namespace Syphon.Setup;
public partial class SetupWindow
{
 internal void BeginAutomaticUpdate(UpdateInvocation request) {
  Next.Click-=NextClick;Next.Click+=(_,_)=>Close();
  Loaded+=async(_,_)=>await RunAutomaticUpdateAsync(request);
 }
 private async Task RunAutomaticUpdateAsync(UpdateInvocation request) {
  busy=true;ShowStep(2);Heading.Text="Updating Syphon";Subtitle.Text="Your downloads, history and preferences will be preserved.";Next.IsEnabled=false;Back.IsEnabled=false;Cancel.IsEnabled=false;
  try {
   Status.Text="Verifying this update and the installed application…";
   var receipt=await Task.Run(()=>{var existing=request.ValidateInstallation();request.ValidatePackage(existing.Version);return existing;});
   using(var parent=await Task.Run(request.ValidateParent)) {Status.Text="Waiting for Syphon to save downloads and close…";await request.ReadyAndWaitAsync(parent,cancellation.Token);}
   Status.Text="Installing the update…";
   var progress=new Progress<(double,string)>(value=>{Progress.Value=value.Item1;Percent.Text=$"{value.Item1:0}%";Status.Text=value.Item2;});
   await Task.Run(()=>{using var payload=InstallEngine.OpenPayload();InstallEngine.Install(payload,receipt.Root,receipt.Browser,receipt.DesktopShortcut,progress,cancellation.Token);});
   installedRoot=receipt.Root;busy=false;ShowStep(4);Heading.Text="Syphon has been updated";FinishNote.Text="Your previous settings and browser connection were preserved.";Next.IsEnabled=true;
   try{SetupAppLauncher.Open(installedRoot);Close();}catch(Exception ex){Error.Text="The update finished, but Syphon could not reopen. Use Open Syphon below, or the Start menu. "+ex.Message;Error.Visibility=Visibility.Visible;}
  }catch(Exception ex){
   busy=false;ShowStep(4);Heading.Text="Update stopped safely";Subtitle.Text="Your downloads and history remain in your own folders.";Status.Text="Update could not finish.";FinishNote.Text="Close setup and open Syphon from the Start menu to retry. If Syphon will not open, download a fresh installer and choose Repair.";Error.Text=ex.Message;Error.Visibility=Visibility.Visible;Next.IsEnabled=true;Next.Content="Close";Cancel.IsEnabled=true;
   installedRoot=request.Root;OpenSyphon.Visibility=Visibility.Visible;OpenSyphon.IsEnabled=true;
  }
 }
}
