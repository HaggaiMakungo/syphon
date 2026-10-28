using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipes;
using System.Text.Json;
namespace Syphon.Setup;
internal sealed record UpdateInvocation(string Root, int ParentId, long ParentStart)
{
 internal string PipeName => $"Syphon.Update.{ParentId}.{ParentStart}";
 internal static UpdateInvocation Parse(string[] args) {
  if(args.Length!=7||args[0]!="--update"||args[2]!="--parent-process"||args[4]!="--parent-start"||args[6]!="--restart"||!int.TryParse(args[3],out var id)||id<=0||!long.TryParse(args[5],out var ticks)||ticks<=0||ticks>DateTime.MaxValue.Ticks)throw new IOException("Invalid update request. Open Syphon and check for updates again.");
  return new(InstallEngine.ValidateRoot(args[1]),id,ticks);
 }
 internal InstallReceipt ValidateInstallation() {
  InstallEngine.NoLinks(Root);
  var marker=Path.Combine(Root,InstallEngine.Marker);
  if(!File.Exists(marker)||(File.GetAttributes(marker)&FileAttributes.ReparsePoint)!=0)throw new IOException("The installation receipt is missing or linked. Run normal setup.");
  var receipt=InstallEngine.ReadReceipt(Root);
  if(!receipt.MachineWide||!SetupMaintenance.ProtectedRoot(Root)||!receipt.Files.Contains("app/Syphon.exe",StringComparer.OrdinalIgnoreCase))throw new IOException("Automatic updates require the installed Program Files edition. Run normal setup to upgrade this copy.");
  foreach(var relative in receipt.Files){var path=InstallEngine.SafePath(Root,relative);InstallEngine.NoLinks(Path.GetDirectoryName(path)!);if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("The installation contains a linked file. Run normal setup.");}
  return receipt;
 }
 internal Process ValidateParent() {
  if(ParentId==Environment.ProcessId)throw new IOException("Setup cannot update itself.");
  Process parent;
  try{parent=Process.GetProcessById(ParentId);}catch(ArgumentException){throw new IOException("Syphon closed before update preparation. Reopen Syphon and try again.");}
  try{
   if(!ParentMatches(parent.Id,parent.StartTime.ToUniversalTime().Ticks,parent.MainModule?.FileName,ParentId,ParentStart,Root))throw new IOException("The requesting Syphon process could not be verified. Reopen Syphon and try again.");
   return parent;
  }catch{parent.Dispose();throw;}
 }
 internal static bool ParentMatches(int actualId,long actualStart,string? image,int expectedId,long expectedStart,string root) => actualId==expectedId&&actualStart==expectedStart&&string.Equals(image,InstallEngine.SafePath(root,"app/Syphon.exe"),StringComparison.OrdinalIgnoreCase);
 internal static bool Newer(string candidate,string installed) {
  static (Version,int) Parse(string text){var p=text.TrimStart('v').Split('-',2);if(!Version.TryParse(p[0],out var version))throw new IOException("Invalid package version.");var beta=int.MaxValue;if(p.Length==2&&(!p[1].StartsWith("beta.")||!int.TryParse(p[1][5..],out beta)||beta<0))throw new IOException("Unsupported package version.");return(version,beta);}
  var a=Parse(candidate);var b=Parse(installed);return a.Item1>b.Item1||a.Item1==b.Item1&&a.Item2>b.Item2;
 }
 internal void ValidatePackage(string installed) {
  using var payload=InstallEngine.OpenPayload();using var zip=new ZipArchive(payload,ZipArchiveMode.Read);var entry=zip.GetEntry("release-manifest.json")??throw new IOException("Update package is missing its manifest.");if(entry.Length>2000000)throw new IOException("Invalid update manifest.");using var input=entry.Open();var manifest=JsonSerializer.Deserialize<PackageManifest>(input)??throw new IOException("Invalid update manifest.");if(!Newer(manifest.Version,installed))throw new IOException("This update is not newer than the installed version. Check for updates again.");
  var required=checked(zip.Entries.Sum(e=>e.Length)+200L*1024*1024);if(new DriveInfo(Path.GetPathRoot(Root)!).AvailableFreeSpace<required)throw new IOException("Free additional space on the installation drive, then retry the update.");
 }
 [System.Runtime.InteropServices.DllImport("kernel32.dll",SetLastError=true)] [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe,out int processId);
 internal async Task ReadyAndWaitAsync(Process parent,CancellationToken token) {
  using var pipe=new NamedPipeClientStream(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
  await pipe.ConnectAsync(15000,token);
  if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out var serverId)||serverId!=ParentId)throw new IOException("The update handoff is not owned by the requesting Syphon process.");
  using(var server=ValidateParent()) {}
  using(var writer=new StreamWriter(pipe,new System.Text.UTF8Encoding(false),leaveOpen:true){AutoFlush=true})await writer.WriteAsync("READY\n");
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(90));
  using var reader=new StreamReader(pipe,System.Text.Encoding.UTF8,false,128,leaveOpen:true);
  await RequireCommitAsync(reader,deadline.Token);
  await WaitForParentAsync(parent,TimeSpan.FromSeconds(90),token);
 }
 internal static async Task RequireCommitAsync(TextReader reader,CancellationToken token) {
  if(await reader.ReadLineAsync(token)!="COMMIT")throw new IOException("Syphon could not finish saving. No application files were changed. Retry the update in Syphon.");
 }
 internal static async Task WaitForParentAsync(Process parent,TimeSpan limit,CancellationToken token) {
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(limit);
  try{await parent.WaitForExitAsync(timeout.Token).ConfigureAwait(false);}catch(OperationCanceledException) when(!token.IsCancellationRequested){throw new IOException("Syphon did not finish saving and close within 90 seconds. No application files were changed. Close Syphon, then check for updates again.");}
 }
}
