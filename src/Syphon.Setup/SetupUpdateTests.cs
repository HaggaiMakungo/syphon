using System.IO;
namespace Syphon.Setup;
internal static class SetupUpdateTests
{
 internal static void Run(){
  static void Check(bool value){if(!value)throw new Exception("Update contract regression");}
  foreach(var args in new[]{Array.Empty<string>(),new[]{"--update","relative","--parent-process","1","--parent-start","1","--restart"},new[]{"--update","C:/","--parent-process","1","--parent-start","1","--restart"},new[]{"--update","C:/Example/Syphon","--parent-process","0","--parent-start","1","--restart"}}){try{UpdateInvocation.Parse(args);throw new Exception("Invalid update accepted");}catch(IOException){}}
  Check(UpdateInvocation.ParentMatches(12,34,"C:/Example/Syphon/app/Syphon.exe".Replace('/',Path.DirectorySeparatorChar),12,34,Path.GetFullPath("C:/Example/Syphon")));
  Check(!UpdateInvocation.ParentMatches(12,35,"C:/Example/Syphon/app/Syphon.exe",12,34,"C:/Example/Syphon"));
  Check(!UpdateInvocation.ParentMatches(13,34,"C:/Example/Syphon/app/Syphon.exe",12,34,"C:/Example/Syphon"));
  Check(!UpdateInvocation.ParentMatches(12,34,"C:/Other/Syphon.exe",12,34,"C:/Example/Syphon"));
  UpdateInvocation.RequireCommitAsync(new StringReader("COMMIT\n"),default).GetAwaiter().GetResult();
  foreach(var message in new[]{"", "ABORT\n", "READY\n"}) {
   try{UpdateInvocation.RequireCommitAsync(new StringReader(message),default).GetAwaiter().GetResult();throw new Exception("Missing commit accepted");}catch(IOException){}
  }
  var fixture=Path.Combine(Path.GetTempPath(),"Syphon.Update.Contract",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
  try {
   var command=new[]{"--update",fixture,"--parent-process","12","--parent-start","34","--restart"};
   var parsed=UpdateInvocation.Parse(command);Check(parsed.Root==Path.GetFullPath(fixture));Check(parsed.PipeName=="Syphon.Update.12.34");
   try{parsed.ValidateInstallation();throw new Exception("Unowned root accepted");}catch(IOException){}
   try{UpdateInvocation.Parse(command.Concat(new[]{"extra"}).ToArray());throw new Exception("Extra argument accepted");}catch(IOException){}
   try{(parsed with {ParentId=Environment.ProcessId}).ValidateParent();throw new Exception("Setup parent accepted");}catch(IOException){}
   using(var waiting=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe","/c ping -n 3 127.0.0.1 >nul"){UseShellExecute=false,CreateNoWindow=true})!) {
    try{UpdateInvocation.WaitForParentAsync(waiting,TimeSpan.FromMilliseconds(25),default).GetAwaiter().GetResult();throw new Exception("Wait exceeded bound without failure");}catch(IOException){}
    Check(!waiting.HasExited);UpdateInvocation.WaitForParentAsync(waiting,TimeSpan.FromSeconds(10),default).GetAwaiter().GetResult();
   }
   using var parent=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe","/c exit 0"){UseShellExecute=false,CreateNoWindow=true})!;parent.WaitForExit();
   try{(parsed with {ParentId=parent.Id}).ValidateParent();throw new Exception("Exited parent accepted");}catch(IOException){}
  } finally {Directory.Delete(fixture);}
  Check(UpdateInvocation.Newer("0.1.0-beta.18","0.1.0-beta.17"));Check(!UpdateInvocation.Newer("0.1.0-beta.17","0.1.0-beta.18"));Check(!UpdateInvocation.Newer("0.1.0-beta.18","0.1.0-beta.18"));
 }
}
