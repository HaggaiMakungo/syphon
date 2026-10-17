using System.IO;
using System.Text.Json;
using Microsoft.Win32;
namespace Syphon.Setup;

internal static class SetupMaintenanceTests
{
 private sealed class RegistryFixture : ISetupRegistry
 {
  internal readonly Dictionary<(RegistryHive,string,string),string> Values=new();
  public string? Read(RegistryHive hive,string key,string name)=>Values.GetValueOrDefault((hive,key,name));
  public void Delete(RegistryHive hive,string key){foreach(var item in Values.Keys.Where(k=>k.Item1==hive&&k.Item2==key).ToArray())Values.Remove(item);}
 }
 private static void Check(bool value,string message){if(!value)throw new Exception(message);}
 internal static void Preview(string destination)
 {
  var root=Path.Combine(Path.GetTempPath(),"Syphon.Setup.Preview",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  File.WriteAllText(Path.Combine(root,InstallEngine.Marker),JsonSerializer.Serialize(new InstallReceipt(InstallEngine.Product,"0.1.0-beta.6",root,["app/Syphon.exe"],true,true,MachineWide:true)));
  var window=new SetupWindow(false,new SetupMaintenance(new RegistryFixture(),[root]));
  window.Closed+=(_,_)=>{if(Directory.Exists(root))Directory.Delete(root,true);};
  window.PrepareMaintenance(false);window.RenderPreview(destination);window.Show();
 }
 internal static void Run(string testRoot)
 {
  var repairRoot=Path.Combine(testRoot,"RepairFixture");
  using(var payload=SetupTests.Fixture())InstallEngine.Install(payload,repairRoot,true,false,new Progress<(double,string)>(),CancellationToken.None,false);
  File.Delete(Path.Combine(repairRoot,"app","Syphon.exe"));
  var saved=InstallEngine.ReadReceipt(repairRoot);
  using(var payload=SetupTests.Fixture())InstallEngine.Install(payload,repairRoot,saved.Browser,saved.DesktopShortcut,new Progress<(double,string)>(),CancellationToken.None,false);
  Check(File.Exists(Path.Combine(repairRoot,"app","Syphon.exe")),"Repair did not restore a missing application file.");
  Check(InstallEngine.ReadReceipt(repairRoot).Browser&&!InstallEngine.ReadReceipt(repairRoot).DesktopShortcut,"Repair changed the existing integration choices.");
  var root=Path.Combine(testRoot,"Maintenance");Directory.CreateDirectory(root);
  var registry=new RegistryFixture();var maintenance=new SetupMaintenance(registry,[root]);
  var receipt=new InstallReceipt(InstallEngine.Product,"0.1.0-beta.1",root,["app/Syphon.exe","Uninstall.exe"],true,false,MachineWide:true);
  File.WriteAllText(Path.Combine(root,InstallEngine.Marker),JsonSerializer.Serialize(receipt));
  Check(maintenance.FindInstallations().Single().Root==root,"Receipt-owned installation was not discovered for repair.");
  var window=new SetupWindow(false,maintenance);
  try{
   Check(((System.Windows.Controls.StackPanel)window.FindName("MaintenancePanel")).Visibility==System.Windows.Visibility.Visible,"Setup hides maintenance for an existing installation.");
   window.PrepareMaintenance(false);
   Check((string)((System.Windows.Controls.Button)window.FindName("Next")).Content=="Upgrade / reinstall","Upgrade is not an explicit action.");
   window.PrepareMaintenance(true);
   Check((string)((System.Windows.Controls.Button)window.FindName("Next")).Content=="Repair","Repair is not an explicit action.");
   Check(((System.Windows.Controls.TextBox)window.FindName("Location")).IsReadOnly,"Repair permits accidental installation relocation.");
   Check(!((System.Windows.Controls.CheckBox)window.FindName("DesktopOption")).IsChecked!.Value,"Repair replaced the previous shortcut choice.");
  }finally{window.Close();}
  var key=SetupMaintenance.UninstallKey;
  registry.Values[(RegistryHive.CurrentUser,key,"DisplayName")]="Syphon";
  registry.Values[(RegistryHive.CurrentUser,key,"InstallLocation")]=root;
  registry.Values[(RegistryHive.CurrentUser,key,"UninstallString")]=SetupMaintenance.UninstallCommand(root);
  Check(maintenance.FindCleanup().Count==0,"Cleanup targets a valid receipt even when app files need repair.");
  File.Delete(Path.Combine(root,InstallEngine.Marker));
  var hostKey=SetupMaintenance.HostKeys[0];
  var manifest=Path.Combine(root,"host","com.syphon.downloads.json");
  registry.Values[(RegistryHive.CurrentUser,hostKey,"")]=manifest;
  Check(maintenance.FindCleanup().Count==2,"Stale uninstall and host registrations were not detected.");
  var foreign=Path.Combine(testRoot,"development","com.syphon.downloads.json");
  registry.Values[(RegistryHive.CurrentUser,SetupMaintenance.HostKeys[1],"")]=foreign;
  Directory.CreateDirectory(Path.Combine(root,"app"));File.WriteAllText(Path.Combine(root,"app","Syphon.exe"),"preserve");
  Check(maintenance.Cleanup()==0,"Cleanup did not recheck restored application files.");
  File.Delete(Path.Combine(root,"app","Syphon.exe"));
  Check(maintenance.Cleanup()==2,"Owned stale registrations were not cleaned.");
  Check(registry.Read(RegistryHive.CurrentUser,SetupMaintenance.HostKeys[1],"")==foreign,"Cleanup removed a development host registration.");
  registry.Values[(RegistryHive.CurrentUser,key,"DisplayName")]="Other application";
  registry.Values[(RegistryHive.CurrentUser,key,"InstallLocation")]=root;
  registry.Values[(RegistryHive.CurrentUser,key,"UninstallString")]=SetupMaintenance.UninstallCommand(root);
  Check(maintenance.Cleanup()==0,"Cleanup removed a foreign application entry.");
  registry.Values[(RegistryHive.CurrentUser,key,"DisplayName")]="Syphon";
  registry.Values[(RegistryHive.CurrentUser,key,"UninstallString")]=@"C:\Other\Uninstall.exe --uninstall";
  Check(maintenance.Cleanup()==0,"Cleanup removed a mismatched uninstaller entry.");
  Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);File.WriteAllText(manifest,"preserve");
  registry.Values[(RegistryHive.CurrentUser,hostKey,"")]=manifest;
  Check(maintenance.Cleanup()==0,"Cleanup removed a host whose manifest still exists.");
  Check(Directory.Exists(root),"Registration cleanup deleted an installation folder.");
 }
}
