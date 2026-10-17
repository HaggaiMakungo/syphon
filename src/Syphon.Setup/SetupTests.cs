using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
namespace Syphon.Setup;
internal static class SetupTests
{
 internal static void Run() {
  var testRoot=Path.Combine(Path.GetTempPath(),"Syphon.Setup.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(testRoot);
  var progress=new Progress<(double,string)>();
  try {
   Check(InstallEngine.DefaultRoot==Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Syphon"),"Default installation must use Program Files.");
   Check(App.IsUninstallInvocation([],@"C:\Program Files\Syphon\Uninstall.exe"),"Direct uninstaller launch was not recognized.");
   Check(App.IsUninstallInvocation(["--uninstall"],"Syphon.Setup.exe"),"Windows uninstall command was not recognized.");
   Check(!App.IsUninstallInvocation([],"Syphon.Setup.exe"),"Normal setup opened removal mode.");
   var helperStart=SetupWindow.CreateUninstallStart(@"C:\Program Files\Syphon-Uninstall-test\Syphon.Uninstall.exe",@"C:\Program Files\Syphon");
   Check(helperStart.WorkingDirectory==@"C:\Program Files\Syphon-Uninstall-test","Removal helper inherits the installation working directory.");
   Check(helperStart.ArgumentList.Count==4&&helperStart.ArgumentList[2]=="--parent-process"&&helperStart.ArgumentList[3]==Environment.ProcessId.ToString(),"Removal helper does not wait for its original process.");
   var removal=new SetupWindow(true);
   try {
    Check(removal.Title=="Uninstall Syphon","Removal must not open with the installer title.");
    Check(removal.FindName("UninstallPanel") is System.Windows.Controls.StackPanel {Visibility:System.Windows.Visibility.Visible},"Removal confirmation is missing.");
    Check(removal.FindName("LocationPanel") is System.Windows.Controls.StackPanel {Visibility:System.Windows.Visibility.Collapsed},"Uninstall shows installation location controls.");
    removal.CompleteRemoval();
    Check(((System.Windows.Controls.TextBlock)removal.FindName("Heading")).Text=="Syphon has been removed","Removal completion is not acknowledged.");
    Check((string)((System.Windows.Controls.Button)removal.FindName("Next")).Content=="Close","Removal completion has no Close action.");
   }finally{removal.Close();}
   var wizard=new SetupWindow();
   try {
    typeof(SetupWindow).GetMethod("ShowStep",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(wizard,[3]);
    Check((string)((System.Windows.Controls.Button)wizard.FindName("Next")).Content!="Finish","Browser setup must precede Finish.");
    Check(wizard.FindName("BrowserPanel") is System.Windows.Controls.StackPanel { Visibility: System.Windows.Visibility.Visible },"Browser instructions must be visible after installation.");
    var next=(System.Windows.Controls.Button)wizard.FindName("Next");Check(!next.IsEnabled,"Continuing requires an explicit browser setup choice.");
    next.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    Check((string)next.Content!="Finish","Programmatic Continue bypassed the mandatory choice.");
    var choice=(System.Windows.Controls.RadioButton)wizard.FindName("SkipBrowser");choice.IsChecked=true;
    Check(next.IsEnabled,"Explicit skip did not enable Continue.");
    next.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    Check((string)next.Content=="Finish","Explicit choice did not reach Finish.");
   } finally {wizard.Close();}
   var target=Path.Combine(testRoot,"Application");
   using(var stream=Fixture())InstallEngine.Install(stream,target,false,false,progress,CancellationToken.None,false);
   Check(File.Exists(Path.Combine(target,"app","Syphon.exe")),"Install did not publish files.");
   Check(InstallEngine.ReadReceipt(target+Path.DirectorySeparatorChar).Product==InstallEngine.Product,"Uninstaller folder trailing separator was rejected.");
   Check(!InstallEngine.ReadReceipt(target).Browser,"Browser integration must remain off until chosen.");
   InstallEngine.EnableBrowserIntegration(target,false);
   Check(InstallEngine.ReadReceipt(target).Browser,"Explicit connection choice did not persist.");
   File.WriteAllText(Path.Combine(target,"user-kept.txt"),"preserve");
   using(var stream=Fixture())InstallEngine.Install(stream,target,false,false,progress,CancellationToken.None,false);
   Check(Directory.EnumerateDirectories(testRoot,".Syphon-previous-*").Any(p=>File.Exists(Path.Combine(p,"user-kept.txt"))),"Upgrade erased an unknown file.");
   File.WriteAllText(Path.Combine(target,"keep.txt"),"preserve");
   // Reproduce Windows denying removal while the original process still owns an installed executable and working directory.
   var ready=Path.Combine(testRoot,"parent-ready");
   static string PsQuote(string value)=>"'"+value.Replace("'","''")+"'";
   var holderStart=new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=target};
   holderStart.ArgumentList.Add("-NoProfile");holderStart.ArgumentList.Add("-NonInteractive");holderStart.ArgumentList.Add("-Command");
   holderStart.ArgumentList.Add("$held=[IO.File]::Open("+PsQuote(Path.Combine(target,"app","Syphon.exe"))+",'Open','Read','Read'); [IO.File]::WriteAllText("+PsQuote(ready)+",'ready'); Start-Sleep -Seconds 2; $held.Dispose()");
   using(var holder=System.Diagnostics.Process.Start(holderStart)!) {
    try {
     var deadline=DateTime.UtcNow.AddSeconds(10);while(!File.Exists(ready)&&DateTime.UtcNow<deadline&&!holder.HasExited)System.Threading.Thread.Sleep(20);
     Check(File.Exists(ready),"Could not create the original uninstaller lock fixture.");
     var locked=false;try{File.Delete(Path.Combine(target,"app","Syphon.exe"));}catch(IOException){locked=true;}
     Check(locked,"Fixture did not reproduce a locked installation file.");
     SetupWindow.WaitForUninstallerExit(holder.Id);Check(holder.HasExited,"Removal started before the original process exited.");
     InstallEngine.Uninstall(target,false);
    } finally {if(!holder.HasExited)holder.Kill();}
   }
   Check(File.Exists(Path.Combine(target,"keep.txt")),"Uninstall erased an unknown file.");
   Check(!File.Exists(Path.Combine(target,"app","Syphon.exe")),"Uninstall retained owned files.");
   using(var corrupt=Fixture(true)) {try{InstallEngine.Install(corrupt,Path.Combine(testRoot,"Corrupt"),false,false,progress,CancellationToken.None,false);throw new Exception("Corruption accepted.");}catch(InvalidDataException){}}
   Check(!Directory.Exists(Path.Combine(testRoot,"Corrupt")),"Corruption left a published installation.");
   try{_=InstallEngine.SafePath(testRoot,"../escape");throw new Exception("Traversal accepted.");}catch(InvalidDataException){}
   using(var cancelled=Fixture()){using var c=new CancellationTokenSource();c.Cancel();try{InstallEngine.Install(cancelled,Path.Combine(testRoot,"Cancelled"),false,false,progress,c.Token,false);throw new Exception("Cancellation ignored.");}catch(OperationCanceledException){}}
   var occupied=Path.Combine(testRoot,"Occupied");Directory.CreateDirectory(occupied);File.WriteAllText(Path.Combine(occupied,"personal.txt"),"keep");try{InstallEngine.ValidateRoot(occupied);throw new Exception("Unowned directory accepted.");}catch(IOException){}
   var distributionRoot=Path.Combine(testRoot,"Distribution");Directory.CreateDirectory(distributionRoot);
   File.WriteAllText(Path.Combine(distributionRoot,"browser-distribution.json"),"{\"chromeExtensionId\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"edgeExtensionId\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"}");
   var distribution=BrowserDistribution.Load(distributionRoot);
   Check(distribution.AllowedOrigins("cccccccccccccccccccccccccccccccc").Length==3,"Store IDs were not allowlisted.");
   Check(distribution.StoreUrl(false)=="https://chromewebstore.google.com/detail/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","Incorrect Chrome store URL.");
   File.WriteAllText(Path.Combine(distributionRoot,"browser-distribution.json"),"{\"chromeExtensionId\":\"https://untrusted.test\"}");
   try{BrowserDistribution.Load(distributionRoot);throw new Exception("Invalid store ID accepted.");}catch(InvalidDataException){}
   var log=Environment.GetEnvironmentVariable("SYPHON_SETUP_TEST_REPORT")??Path.Combine(Path.GetTempPath(),"Syphon-setup-tests.txt");File.WriteAllText(log,"PASS: staging, verification, upgrade, unknown-file preservation, uninstall, corrupt package, traversal, cancellation and unowned directory checks.\n");
  } finally {
   var full=Path.GetFullPath(testRoot);if(full.StartsWith(Path.Combine(Path.GetTempPath(),"Syphon.Setup.Tests")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))Directory.Delete(full,true);
  }
 }
 internal static void VerifyPackage(string target) {
  if(Directory.Exists(target))throw new IOException("Package test requires a new dedicated target.");
  using(var payload=InstallEngine.OpenPayload())InstallEngine.Install(payload,target,false,false,new Progress<(double,string)>(),CancellationToken.None,false);
  var versions=new Dictionary<string,string>();
  foreach(var tool in new[]{"ffmpeg","ffprobe","yt-dlp","node"}) {
   var start=new System.Diagnostics.ProcessStartInfo(Path.Combine(target,"app",".tools","media",tool+".exe"),tool is "ffmpeg" or "ffprobe"?"-version":"--version"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
   using var process=System.Diagnostics.Process.Start(start)!;var output=process.StandardOutput.ReadToEnd();var error=process.StandardError.ReadToEnd();if(!process.WaitForExit(30000)||process.ExitCode!=0)throw new IOException(tool+" could not run: "+error);versions[tool]=output.Split('\n')[0];
  }
  File.WriteAllText(target+".report.json",JsonSerializer.Serialize(new{passed=true,checks="Full payload extraction, file hashes, native media tool execution, generated installed paths",versions},new JsonSerializerOptions{WriteIndented=true}));
 }
 internal static void RenderBrowser(string root,string output) {
  var window=new BrowserSetupWindow(root);window.Show();window.UpdateLayout();
  var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);using var file=File.Create(output);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));encoder.Save(file);window.Close();
 }
 private static MemoryStream Fixture(bool corrupt=false) {
  var files=new Dictionary<string,byte[]> { ["app/Syphon.exe"]=[1,2,3], ["host/Syphon.NativeHost.exe"]=[4,5,6], ["Uninstall.exe"]=[7,8], ["extensions/manifest.json"]=System.Text.Encoding.UTF8.GetBytes("{\"key\":\"AQIDBA==\"}") };
  var manifest=new PackageManifest("0.1.0-test",files.ToDictionary(k=>k.Key,k=>new PackageFile(k.Value.Length,Convert.ToHexString(SHA256.HashData(k.Value)))));
  var stream=new MemoryStream();using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true)) {
   foreach(var pair in files){using var output=zip.CreateEntry(pair.Key).Open();output.Write(corrupt&&pair.Key=="app/Syphon.exe"?[9,9,9]:pair.Value);}
   using var json=zip.CreateEntry("release-manifest.json").Open();JsonSerializer.Serialize(json,manifest);
  }
  stream.Position=0;return stream;
 }
 private static void Check(bool value,string error){if(!value)throw new Exception(error);}
}
