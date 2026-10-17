using System.IO;
using System.Text.Json;
using Microsoft.Win32;
namespace Syphon.Setup;

internal interface ISetupRegistry
{
 string? Read(RegistryHive hive,string key,string name);
 void Delete(RegistryHive hive,string key);
}
internal sealed class SetupRegistry : ISetupRegistry
{
 public string? Read(RegistryHive hive,string key,string name){using var registry=RegistryKey.OpenBaseKey(hive,RegistryView.Registry64);using var entry=registry.OpenSubKey(key);return entry?.GetValue(name) as string;}
 public void Delete(RegistryHive hive,string key){using var registry=RegistryKey.OpenBaseKey(hive,RegistryView.Registry64);registry.DeleteSubKey(key,false);}
}
internal sealed record CleanupRegistration(RegistryHive Hive,string Key,string Description);
internal sealed class SetupMaintenance
{
 internal const string UninstallKey=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Syphon.Desktop";
 internal static readonly string[] HostKeys=[@"Software\Google\Chrome\NativeMessagingHosts\com.syphon.downloads",@"Software\Microsoft\Edge\NativeMessagingHosts\com.syphon.downloads"];
 private static readonly RegistryHive[] Hives=[RegistryHive.LocalMachine,RegistryHive.CurrentUser];
 private readonly ISetupRegistry registry;
 private readonly string[] knownRoots;
 internal SetupMaintenance(ISetupRegistry? registry=null,string[]? roots=null){this.registry=registry??new SetupRegistry();knownRoots=(roots??[InstallEngine.DefaultRoot,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Syphon")]).Select(Normalize).ToArray();}
 private static string Normalize(string path)=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
 internal static string UninstallCommand(string root)=>"\""+Path.Combine(root,"Uninstall.exe")+"\" --uninstall";
 private bool KnownRoot(string root)=>knownRoots.Contains(Normalize(root),StringComparer.OrdinalIgnoreCase);
 internal static bool ProtectedRoot(string root)=>Normalize(root).StartsWith(Normalize(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
 internal List<InstallReceipt> FindInstallations()
 {
  var roots=knownRoots.Concat(Hives.Select(h=>registry.Read(h,UninstallKey,"InstallLocation"))).Where(r=>!string.IsNullOrWhiteSpace(r));
  var result=new List<InstallReceipt>();
  foreach(var value in roots){try{var root=Normalize(value!);if(!Path.IsPathFullyQualified(value!)||(!KnownRoot(root)&&!ProtectedRoot(root)))continue;InstallEngine.NoLinks(root);var receipt=InstallEngine.ReadReceipt(root);if(!result.Any(r=>r.Root.Equals(receipt.Root,StringComparison.OrdinalIgnoreCase)))result.Add(receipt);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException){}}
  return result;
 }
 private bool AbsentInstallation(string root)
 {
  InstallEngine.NoLinks(root);
  return !Present(Path.Combine(root,InstallEngine.Marker))&&!Present(Path.Combine(root,"app","Syphon.exe"))&&!Present(Path.Combine(root,"Uninstall.exe"));
 }
 private static bool Present(string path){try{_=File.GetAttributes(path);return true;}catch(FileNotFoundException){return false;}catch(DirectoryNotFoundException){return false;}}
 internal List<CleanupRegistration> FindCleanup()
 {
  var result=new List<CleanupRegistration>();
  foreach(var hive in Hives){
   var location=registry.Read(hive,UninstallKey,"InstallLocation");
   if(location is not null&&Path.IsPathFullyQualified(location)){
    try{var root=Normalize(location);if(KnownRoot(root)&&AbsentInstallation(root)&&registry.Read(hive,UninstallKey,"DisplayName")=="Syphon"&&string.Equals(registry.Read(hive,UninstallKey,"UninstallString"),UninstallCommand(root),StringComparison.OrdinalIgnoreCase))result.Add(new(hive,UninstallKey,"Broken Syphon entry in Windows Installed apps"));}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){}
   }
   foreach(var root in knownRoots){
    try{if(!AbsentInstallation(root))continue;var manifest=Path.Combine(root,"host","com.syphon.downloads.json");if(Present(manifest))continue;
     foreach(var key in HostKeys)if(string.Equals(registry.Read(hive,key,""),manifest,StringComparison.OrdinalIgnoreCase))result.Add(new(hive,key,"Broken "+(key.Contains("Chrome")?"Chrome":"Edge")+" connection to the missing installation"));
    }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}
   }
  }
  return result;
 }
 internal int Cleanup()
 {
  var removed=0;
  foreach(var candidate in FindCleanup()){
   // Recheck file ownership and registry values immediately before each exact-key removal.
   if(!FindCleanup().Any(item=>item.Hive==candidate.Hive&&item.Key==candidate.Key))continue;
   registry.Delete(candidate.Hive,candidate.Key);removed++;
  }
  return removed;
 }
}
