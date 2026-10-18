using System.IO;
using System.Runtime.InteropServices;

namespace Syphon.Setup;

internal static class SetupAppLauncher
{
 internal static void Open(string root) => Open(root, ExecuteFromDesktop);

 internal static void Open(string root, Action<string,string> shellExecute)
 {
  if(string.IsNullOrWhiteSpace(root))throw new IOException("The installation folder is unavailable. Open Syphon from the Start menu.");
  var receipt=InstallEngine.ReadReceipt(root);
  var executable=InstallEngine.SafePath(receipt.Root,"app/Syphon.exe");
  InstallEngine.NoLinks(receipt.Root);InstallEngine.NoLinks(Path.GetDirectoryName(executable)!);
  if(!receipt.Files.Contains("app/Syphon.exe",StringComparer.OrdinalIgnoreCase)||!File.Exists(executable))
   throw new IOException("Syphon's application file is missing. Repair the installation and try again.");
  if((File.GetAttributes(executable)&FileAttributes.ReparsePoint)!=0)throw new IOException("The application path cannot be a link.");
  shellExecute(executable,Path.GetDirectoryName(executable)!);
 }

 // Obtain the existing desktop's Shell.Application, not a new elevated shell.
 // https://devblogs.microsoft.com/oldnewthing/20131118-00/?p=2643
 internal static void ExecuteFromDesktop(string executable,string directory)
 {
  object? windows=null,desktop=null,document=null,shell=null;
  try
  {
   var type=Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"),true)!;
   windows=Activator.CreateInstance(type)??throw new IOException("Windows Explorer is unavailable.");
   object location=0,root=0;int handle;
   desktop=((dynamic)windows).FindWindowSW(ref location,ref root,8,out handle,1);
   if(desktop is null)throw new IOException("Windows Explorer is unavailable.");
   document=((dynamic)desktop).Document;
   shell=((dynamic)document).Application;
   ((dynamic)shell).ShellExecute(executable,"",directory,"open",1);
  }
  catch(Exception ex)
  {
   throw new IOException("Windows could not open Syphon from your desktop. You can still finish setup and open it from the Start menu.",ex);
  }
  finally
  {
   foreach(var value in new[]{shell,document,desktop,windows})
    if(value is not null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);
  }
 }
}
