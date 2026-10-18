using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
namespace Syphon.Setup;

// Runs only in the separately built development checker. Never load a production
// assembly into this process: inspect its metadata, then exercise its real entry point.
internal static class SetupProductionTests
{
 internal static void Verify(string assembly, string dotnet, string report) {
  assembly=Path.GetFullPath(assembly);
  using(var file=File.OpenRead(assembly))
  using(var pe=new PEReader(file)) {
   var metadata=pe.GetMetadataReader();
   foreach(var handle in metadata.TypeDefinitions) {
    var type=metadata.GetTypeDefinition(handle);var name=metadata.GetString(type.Name);
    if(name is "SetupTests" or "SetupMaintenanceTests" or "SetupProductionTests")throw new Exception("Production contains diagnostic type: "+name);
    foreach(var fieldHandle in type.GetFields())
     if(name=="PackagePayload"&&metadata.GetString(metadata.GetFieldDefinition(fieldHandle).Name)=="TestSource")throw new Exception("Production allows an external payload source.");
    foreach(var methodHandle in type.GetMethods())
     if(metadata.GetString(metadata.GetMethodDefinition(methodHandle).Name)=="RenderPreview")throw new Exception("Production contains preview rendering.");
   }
  }
  var scratch=Path.Combine(Path.GetTempPath(),"Syphon.Setup.Production.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
  try {
   string[][] rejected=[
    ["--self-test"], ["--verify-package",Path.Combine(scratch,"verify"),Path.Combine(scratch,"payload")],
    ["--verify-package"], ["--verify-desktop-launch",Path.Combine(scratch,"desktop")],
    ["--render",Path.Combine(scratch,"render")], ["--render"],
    ["--render-browser",scratch,Path.Combine(scratch,"browser.png")], ["--render-maintenance",scratch],
    ["--verify-production",assembly,dotnet,Path.Combine(scratch,"report")],
    ["--unknown"], ["--uninstall","--self-test"], ["--remove-from"],
    ["--remove-from",scratch,"--parent-process","invalid"], ["--remove-from",scratch,"--parent-process","-1"],
    ["--remove-from",scratch,"--self-test"], ["unexpected-file"]];
   foreach(var args in rejected) {
    var start=new ProcessStartInfo(dotnet){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=scratch};
    start.ArgumentList.Add(assembly);foreach(var arg in args)start.ArgumentList.Add(arg);
    using var process=Process.Start(start)??throw new Exception("Cannot launch production argument probe.");
    if(!process.WaitForExit(10000)){process.Kill(true);throw new Exception("Production opened UI or stalled for "+args[0]);}
    if(process.ExitCode!=2)throw new Exception("Production must reject "+args[0]+" with exit 2, received "+process.ExitCode);
    if(Directory.EnumerateFileSystemEntries(scratch).Any())throw new Exception("Rejected arguments wrote output: "+args[0]);
   }
  }finally{Directory.Delete(scratch,true);}
  File.WriteAllText(report,"Production diagnostic isolation and fail-closed argument checks passed.");
 }
}
