using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Text.Json;
namespace Syphon.Setup;
internal static class WindowsIntegration
{
 private const string UninstallKey=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Syphon.Desktop";
 private static readonly string[] HostKeys=[@"Software\Google\Chrome\NativeMessagingHosts\com.syphon.downloads",@"Software\Microsoft\Edge\NativeMessagingHosts\com.syphon.downloads"];
 internal static bool WebViewPresent() {
  const string key=@"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
  foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine}) foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64}) {
   using var baseKey=RegistryKey.OpenBaseKey(hive,view);using var client=baseKey.OpenSubKey(key);
   if(Version.TryParse(client?.GetValue("pv") as string,out var version)&&version>new Version(0,0,0,0))return true;
  }
  return false;
 }
 internal static Action Register(InstallReceipt receipt) {
  var registry=receipt.MachineWide?Registry.LocalMachine:Registry.CurrentUser;
  var undo=new List<Action>();
  try {
   if(receipt.Browser)foreach(var path in HostKeys) {
    // A user's earlier beta/dev registration takes precedence over HKLM.
    // Update only an existing override for this host; restore it on failure.
    if(receipt.MachineWide) {
     using var oldUser=Registry.CurrentUser.OpenSubKey(path);var previousUser=oldUser?.GetValue("") as string;
     if(previousUser is not null) {
      undo.Add(()=>{using var key=Registry.CurrentUser.CreateSubKey(path);key.SetValue("",previousUser);});
      using var user=Registry.CurrentUser.CreateSubKey(path);user.SetValue("",Path.Combine(receipt.Root,"host","com.syphon.downloads.json"));
     }
    }

    using var old=registry.OpenSubKey(path);var previous=old?.GetValue("") as string;
    undo.Add(()=>{if(previous is null)registry.DeleteSubKeyTree(path,false);else{using var k=registry.CreateSubKey(path);k.SetValue("",previous);}});
    using var key=registry.CreateSubKey(path);key.SetValue("",Path.Combine(receipt.Root,"host","com.syphon.downloads.json"));
   }
   using(var old=registry.OpenSubKey(UninstallKey)) {
    var values=old?.GetValueNames().ToDictionary(n=>n,n=>(old.GetValue(n)!,old.GetValueKind(n)));
    undo.Add(()=>{registry.DeleteSubKeyTree(UninstallKey,false);if(values is not null){using var key=registry.CreateSubKey(UninstallKey);foreach(var item in values)key.SetValue(item.Key,item.Value.Item1,item.Value.Item2);}});
   }
   using(var key=registry.CreateSubKey(UninstallKey)) {
    key.SetValue("DisplayName","Syphon");key.SetValue("DisplayVersion",receipt.Version);key.SetValue("InstallLocation",receipt.Root);key.SetValue("DisplayIcon",Path.Combine(receipt.Root,"app","Syphon.exe"));
    key.SetValue("UninstallString","\""+Path.Combine(receipt.Root,"Uninstall.exe")+"\" --uninstall");key.SetValue("NoModify",1);key.SetValue("NoRepair",1);
   }
   var start=Path.Combine(Environment.GetFolderPath(receipt.MachineWide?Environment.SpecialFolder.CommonPrograms:Environment.SpecialFolder.Programs),"Syphon.lnk");
   CreateShortcut(start,Path.Combine(receipt.Root,"app","Syphon.exe"),undo);
   if(receipt.DesktopShortcut)CreateShortcut(Path.Combine(Environment.GetFolderPath(receipt.MachineWide?Environment.SpecialFolder.CommonDesktopDirectory:Environment.SpecialFolder.DesktopDirectory),"Syphon.lnk"),Path.Combine(receipt.Root,"app","Syphon.exe"),undo);
   return ()=>{foreach(var action in undo.AsEnumerable().Reverse())action();};
  } catch {foreach(var action in undo.AsEnumerable().Reverse()){try{action();}catch{}}throw;}
 }
 private static void CreateShortcut(string path,string target,List<Action> undo) {
  var previous=File.Exists(path)?File.ReadAllBytes(path):null;
  undo.Add(()=>{if(previous is null)File.Delete(path);else File.WriteAllBytes(path,previous);});
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);
  var shellType=Type.GetTypeFromProgID("WScript.Shell")??throw new IOException("Windows shortcuts are unavailable.");
  dynamic shell=Activator.CreateInstance(shellType)!;object? link=null;
  try {dynamic shortcut=shell.CreateShortcut(path);link=shortcut;shortcut.TargetPath=target;shortcut.WorkingDirectory=Path.GetDirectoryName(target);shortcut.Description="Syphon download manager";shortcut.Save();}
  finally{if(link is not null)Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
 }
 internal static void Remove(InstallReceipt receipt) {
  using(var startup=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)) {
   var expected="\""+Path.Combine(receipt.Root,"app","Syphon.exe")+"\" --startup";
   if(string.Equals(startup?.GetValue("Syphon") as string,expected,StringComparison.OrdinalIgnoreCase))startup!.DeleteValue("Syphon",false);
  }
  var registry=receipt.MachineWide?Registry.LocalMachine:Registry.CurrentUser;
  var manifest=Path.Combine(receipt.Root,"host","com.syphon.downloads.json");
  if(receipt.MachineWide)foreach(var path in HostKeys){using var user=Registry.CurrentUser.OpenSubKey(path);if(string.Equals(user?.GetValue("") as string,manifest,StringComparison.OrdinalIgnoreCase))Registry.CurrentUser.DeleteSubKeyTree(path,false);}
  foreach(var path in HostKeys){using var key=registry.OpenSubKey(path);if(string.Equals(key?.GetValue("") as string,manifest,StringComparison.OrdinalIgnoreCase))registry.DeleteSubKeyTree(path,false);}
  using(var key=registry.OpenSubKey(UninstallKey)){if(string.Equals(key?.GetValue("InstallLocation") as string,receipt.Root,StringComparison.OrdinalIgnoreCase))registry.DeleteSubKeyTree(UninstallKey,false);}
  foreach(var path in new[]{Path.Combine(Environment.GetFolderPath(receipt.MachineWide?Environment.SpecialFolder.CommonPrograms:Environment.SpecialFolder.Programs),"Syphon.lnk"),Path.Combine(Environment.GetFolderPath(receipt.MachineWide?Environment.SpecialFolder.CommonDesktopDirectory:Environment.SpecialFolder.DesktopDirectory),"Syphon.lnk")})RemoveShortcut(path,Path.Combine(receipt.Root,"app","Syphon.exe"));
 }
 private static void RemoveShortcut(string path,string target) {
  if(!File.Exists(path))return;var type=Type.GetTypeFromProgID("WScript.Shell");if(type is null)return;
  dynamic shell=Activator.CreateInstance(type)!;object? link=null;
  try{dynamic shortcut=shell.CreateShortcut(path);link=shortcut;if(string.Equals((string)shortcut.TargetPath,target,StringComparison.OrdinalIgnoreCase))File.Delete(path);}
  finally{if(link is not null)Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
 }
}
