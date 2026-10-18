using System.Windows;
namespace Syphon.Setup;
public partial class App : Application
{
 internal static bool IsUninstallInvocation(string[] args,string executable) => args.Contains("--uninstall") || args.FirstOrDefault()=="--remove-from" || System.IO.Path.GetFileName(executable).Equals("Uninstall.exe",StringComparison.OrdinalIgnoreCase) || System.IO.Path.GetFileName(executable).Equals("Syphon.Uninstall.exe",StringComparison.OrdinalIgnoreCase);
 protected override void OnStartup(StartupEventArgs e) {
  base.OnStartup(e);
  if(e.Args.FirstOrDefault()=="--verify-desktop-launch"&&e.Args.Length==2){try{SetupTests.VerifyDesktopLaunch(e.Args[1]);Shutdown(0);}catch(Exception ex){System.IO.File.WriteAllText(e.Args[1]+".error.txt",ex.ToString());Shutdown(1);}return;}
  if(e.Args.FirstOrDefault()=="--self-test") { ShutdownMode=ShutdownMode.OnExplicitShutdown;try { SetupTests.Run(); Shutdown(0); } catch(Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"Syphon-setup-test-error.txt"),ex.ToString()); Shutdown(1); } return; }
  if(e.Args.FirstOrDefault()=="--verify-package" && e.Args.Length>=2) { try { if(e.Args.Length==3)PackagePayload.TestSource=e.Args[2]; SetupTests.VerifyPackage(e.Args[1]); Shutdown(0); } catch(Exception ex) {System.IO.File.WriteAllText(e.Args[1]+".error.txt",ex.ToString());Shutdown(1);} return; }
  if(e.Args.FirstOrDefault()=="--render-browser" && e.Args.Length==3) { SetupTests.RenderBrowser(e.Args[1],e.Args[2]);Shutdown(0);return; }
  if(e.Args.FirstOrDefault()=="--render-maintenance"&&e.Args.Length==2){SetupMaintenanceTests.Preview(e.Args[1]);return;}
  MainWindow=new SetupWindow(IsUninstallInvocation(e.Args,Environment.ProcessPath ?? ""));
  if(e.Args.FirstOrDefault()=="--remove-from" && (e.Args.Length==2 || (e.Args.Length==4 && e.Args[2]=="--parent-process" && int.TryParse(e.Args[3],out _)))){((SetupWindow)MainWindow).RemoveFrom(e.Args[1],e.Args.Length==4?int.Parse(e.Args[3]):0);((SetupWindow)MainWindow).ContinueConfirmedRemoval();}
  if(e.Args.FirstOrDefault()=="--render")((SetupWindow)MainWindow).RenderPreview(e.Args[1]);
  MainWindow.Show();

 }
}
