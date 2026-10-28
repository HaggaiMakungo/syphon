using System.Windows;
namespace Syphon.Setup;
public partial class App : Application
{
 internal static bool IsUninstallInvocation(string[] args,string executable) => args.Contains("--uninstall") || args.FirstOrDefault()=="--remove-from" || System.IO.Path.GetFileName(executable).Equals("Uninstall.exe",StringComparison.OrdinalIgnoreCase) || System.IO.Path.GetFileName(executable).Equals("Syphon.Uninstall.exe",StringComparison.OrdinalIgnoreCase);
 internal static bool IsSupportedInvocation(string[] args) => IsUpdateInvocation(args) || args.Length==0
  || (args.Length==1&&args[0]=="--uninstall")
  || (args.Length==2&&args[0]=="--remove-from"&&!string.IsNullOrWhiteSpace(args[1]))
  || (args.Length==4&&args[0]=="--remove-from"&&!string.IsNullOrWhiteSpace(args[1])&&args[2]=="--parent-process"&&int.TryParse(args[3],out var process)&&process>0);
 internal static bool IsUpdateInvocation(string[] args) {if(args.FirstOrDefault()!="--update")return false;try{UpdateInvocation.Parse(args);return true;}catch{return false;}}
 protected override void OnStartup(StartupEventArgs e) {
  base.OnStartup(e);
#if SETUP_DIAGNOSTICS
  if(e.Args.FirstOrDefault()=="--verify-production"&&e.Args.Length==4){try{SetupProductionTests.Verify(e.Args[1],e.Args[2],e.Args[3]);Shutdown(0);}catch(Exception ex){System.IO.File.WriteAllText(e.Args[3]+".error.txt",ex.ToString());Shutdown(1);}return;}
  if(e.Args.FirstOrDefault()=="--verify-desktop-launch"&&e.Args.Length==2){try{SetupTests.VerifyDesktopLaunch(e.Args[1]);Shutdown(0);}catch(Exception ex){System.IO.File.WriteAllText(e.Args[1]+".error.txt",ex.ToString());Shutdown(1);}return;}
  if(e.Args.FirstOrDefault()=="--update-self-test"){try{SetupUpdateTests.Run();Shutdown(0);}catch(Exception ex){System.IO.File.WriteAllText(e.Args.Length>1?e.Args[1]:System.IO.Path.Combine(System.IO.Path.GetTempPath(),"Syphon-update-test-error.txt"),ex.ToString());Shutdown(1);}return;}
  if(e.Args.FirstOrDefault()=="--self-test") { ShutdownMode=ShutdownMode.OnExplicitShutdown;try { SetupTests.Run(); Shutdown(0); } catch(Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"Syphon-setup-test-error.txt"),ex.ToString()); Shutdown(1); } return; }
  if(e.Args.FirstOrDefault()=="--verify-package" && e.Args.Length>=2) { try { if(e.Args.Length==3)PackagePayload.TestSource=e.Args[2]; SetupTests.VerifyPackage(e.Args[1]); Shutdown(0); } catch(Exception ex) {System.IO.File.WriteAllText(e.Args[1]+".error.txt",ex.ToString());Shutdown(1);} return; }
  if(e.Args.FirstOrDefault()=="--render-browser" && e.Args.Length==3) { SetupTests.RenderBrowser(e.Args[1],e.Args[2]);Shutdown(0);return; }
  if(e.Args.FirstOrDefault()=="--render-maintenance"&&e.Args.Length==2){SetupMaintenanceTests.Preview(e.Args[1]);return;}
  if(e.Args.FirstOrDefault()=="--render"&&e.Args.Length==2){var preview=new SetupWindow();MainWindow=preview;preview.RenderPreview(e.Args[1]);preview.Show();return;}
#endif
  if(!IsSupportedInvocation(e.Args)){Shutdown(2);return;}
  if(e.Args.FirstOrDefault()=="--update") {var window=new SetupWindow();MainWindow=window;window.BeginAutomaticUpdate(UpdateInvocation.Parse(e.Args));window.Show();return;}
  MainWindow=new SetupWindow(IsUninstallInvocation(e.Args,Environment.ProcessPath ?? ""));
  if(e.Args.FirstOrDefault()=="--remove-from" && (e.Args.Length==2 || (e.Args.Length==4 && e.Args[2]=="--parent-process" && int.TryParse(e.Args[3],out _)))){((SetupWindow)MainWindow).RemoveFrom(e.Args[1],e.Args.Length==4?int.Parse(e.Args[3]):0);((SetupWindow)MainWindow).ContinueConfirmedRemoval();}
  MainWindow.Show();

 }
}
