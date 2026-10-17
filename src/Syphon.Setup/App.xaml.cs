using System.Windows;
namespace Syphon.Setup;
public partial class App : Application
{
 protected override void OnStartup(StartupEventArgs e) {
  base.OnStartup(e);
  if(e.Args.FirstOrDefault()=="--self-test") { try { SetupTests.Run(); Shutdown(0); } catch(Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"Syphon-setup-test-error.txt"),ex.ToString()); Shutdown(1); } return; }
  if(e.Args.FirstOrDefault()=="--verify-package" && e.Args.Length>=2) { try { if(e.Args.Length==3)PackagePayload.TestSource=e.Args[2]; SetupTests.VerifyPackage(e.Args[1]); Shutdown(0); } catch(Exception ex) {System.IO.File.WriteAllText(e.Args[1]+".error.txt",ex.ToString());Shutdown(1);} return; }
  MainWindow=new SetupWindow(e.Args.Contains("--uninstall") || e.Args.FirstOrDefault()=="--remove-from");
  if(e.Args.FirstOrDefault()=="--remove-from" && e.Args.Length==2)((SetupWindow)MainWindow).RemoveFrom(e.Args[1]);
  if(e.Args.FirstOrDefault()=="--render")((SetupWindow)MainWindow).RenderPreview(e.Args[1]);
  MainWindow.Show();

 }
}
