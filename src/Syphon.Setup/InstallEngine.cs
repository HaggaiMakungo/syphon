using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace Syphon.Setup;
internal sealed record PackageFile(long Length, string Sha256);
internal sealed record PackageManifest(string Version, Dictionary<string, PackageFile> Files);
internal sealed record InstallReceipt(string Product, string Version, string Root, string[] Files, bool Browser, bool DesktopShortcut);
internal static class InstallEngine
{
 internal const string Product="Syphon.Desktop", Marker=".syphon-install.json";
 internal static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Syphon");
 internal static Stream OpenPayload() => PackagePayload.Open();
 internal static string SafePath(string root,string relative) {
  if(string.IsNullOrWhiteSpace(relative)||relative.Contains('\\')||relative.Contains(':')||relative.Split('/').Any(p=>p is "" or "." or "..")||Path.IsPathRooted(relative)) throw new InvalidDataException("The package contains an unsafe path.");
  var path=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
  if(!path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package path escaped its destination.");
  return path;
 }
 internal static void NoLinks(string path) {
  for(var d=new DirectoryInfo(path);d is not null;d=d.Parent)
   if(d.Exists&&(d.Attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("Choose a folder without symbolic links or directory junctions.");
 }
 internal static string ValidateRoot(string value) {
  if(!Path.IsPathFullyQualified(value)||value.StartsWith("\\\\",StringComparison.Ordinal)) throw new IOException("Choose a full path on a local drive.");
  var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
  if(Path.GetPathRoot(root)==root||root.Length<8) throw new IOException("Choose a dedicated application folder.");
  NoLinks(root);
  var data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Syphon");
  if(root.Equals(data,StringComparison.OrdinalIgnoreCase)||data.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||root.StartsWith(data+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("The application cannot be installed in its history and settings folder.");
  if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any()) ReadReceipt(root);
  return root;
 }
 internal static InstallReceipt ReadReceipt(string root) {
  var receipt=JsonSerializer.Deserialize<InstallReceipt>(File.ReadAllText(Path.Combine(root,Marker)))??throw new IOException("This folder is not an owned Syphon installation.");
  if(receipt.Product!=Product||!string.Equals(Path.GetFullPath(receipt.Root),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose an empty folder or an existing Syphon installation.");
  foreach(var file in receipt.Files) _=SafePath(root,file);
  return receipt;
 }
 internal static void CheckRunning(string root) {
  foreach(var process in Process.GetProcessesByName("Syphon").Concat(Process.GetProcessesByName("Syphon.NativeHost"))) {
   using(process) {
    string? executable;
    try { executable=process.MainModule?.FileName; } catch { throw new IOException("Close Syphon and its browser handoffs before continuing."); }
    if(executable is null||executable.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Close Syphon before changing this installation. Your downloads and history will be preserved.");
   }
  }
 }
 internal static PackageManifest Extract(Stream stream,string root,IProgress<(double,string)> progress,CancellationToken token) {
  using var zip=new ZipArchive(stream,ZipArchiveMode.Read,leaveOpen:true);
  var entry=zip.GetEntry("release-manifest.json")??throw new InvalidDataException("Package manifest missing.");
  if(entry.Length>2_000_000) throw new InvalidDataException("Package manifest is too large.");
  using var manifestStream=entry.Open();var manifest=JsonSerializer.Deserialize<PackageManifest>(manifestStream)??throw new InvalidDataException("Invalid package manifest.");
  if(manifest.Files.Count is 0 or >10000||zip.Entries.Count!=manifest.Files.Count+1) throw new InvalidDataException("Unexpected package contents.");
  long total=0;foreach(var f in manifest.Files.Values) { if(f.Length<0||f.Length>2_000_000_000||f.Sha256.Length!=64) throw new InvalidDataException("Invalid file metadata.");total=checked(total+f.Length); }
  if(total>8_000_000_000) throw new InvalidDataException("Package is too large.");
  var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long complete=0;
  foreach(var item in zip.Entries) {
   if(item.FullName=="release-manifest.json") continue;
   token.ThrowIfCancellationRequested();var destination=SafePath(root,item.FullName);
   if(!names.Add(item.FullName)||!manifest.Files.TryGetValue(item.FullName,out var expected)||expected.Length!=item.Length||((item.ExternalAttributes>>16)&0xF000)==0xA000) throw new InvalidDataException("Unexpected or duplicate package file.");
   Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
   using(var input=item.Open()) using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None)) using(var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256)) {
    var buffer=new byte[131072];long written=0;int read;
    while((read=input.Read(buffer))!=0) { token.ThrowIfCancellationRequested(); written+=read;if(written>expected.Length)throw new InvalidDataException("Package length mismatch.");output.Write(buffer,0,read);hash.AppendData(buffer,0,read);complete+=read;progress.Report((total==0?90:complete*90d/total,"Installing "+Path.GetFileName(destination))); }
    output.Flush(true);
    if(written!=expected.Length||!Convert.ToHexString(hash.GetHashAndReset()).Equals(expected.Sha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("A package file failed integrity verification. Download a fresh installer.");
   }
  }
  return manifest;
 }
 internal static InstallReceipt Install(Stream payload,string destination,bool browser,bool desktop,IProgress<(double,string)> progress,CancellationToken token,bool integrate=true) {
  var root=ValidateRoot(destination);CheckRunning(root);
  var parent=Path.GetDirectoryName(root)!;Directory.CreateDirectory(parent);
  var stage=Path.Combine(parent,".Syphon-stage-"+Guid.NewGuid().ToString("N"));
  var backup=Path.Combine(parent,".Syphon-previous-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(stage);bool switched=false,backed=false;
  Action? undo=null;
  try {
   var manifest=Extract(payload,stage,progress,token);
   foreach(var file in new[]{"app/Syphon.exe","host/Syphon.NativeHost.exe","Uninstall.exe","extensions/manifest.json"}) if(!File.Exists(SafePath(stage,file))) throw new InvalidDataException("The package is incomplete.");
   var files=manifest.Files.Keys.ToList();
   var extension=JsonDocument.Parse(File.ReadAllText(Path.Combine(stage,"extensions","manifest.json")));
   using(extension) {
    var key=extension.RootElement.GetProperty("key").GetString()!;
    var id=string.Concat(SHA256.HashData(Convert.FromBase64String(key)).Take(16).SelectMany(b=>new[]{(char)('a'+(b>>4)),(char)('a'+(b&15))}));
    var origins=BrowserDistribution.Load(stage).AllowedOrigins(id);
    File.WriteAllText(Path.Combine(stage,"host","host-settings.json"),JsonSerializer.Serialize(new{desktopPath=Path.Combine(root,"app","Syphon.exe"),allowedOrigins=origins}));
    File.WriteAllText(Path.Combine(stage,"host","com.syphon.downloads.json"),JsonSerializer.Serialize(new{name="com.syphon.downloads",description="Syphon browser connection",path=Path.Combine(root,"host","Syphon.NativeHost.exe"),type="stdio",allowed_origins=origins}));
    files.AddRange(["host/host-settings.json","host/com.syphon.downloads.json"]);
   }
   var receipt=new InstallReceipt(Product,manifest.Version,root,files.ToArray(),browser,desktop);
   File.WriteAllText(Path.Combine(stage,Marker),JsonSerializer.Serialize(receipt));
   token.ThrowIfCancellationRequested();CheckRunning(root);NoLinks(root);
   if(Directory.Exists(root)) { Directory.Move(root,backup);backed=true; }
   Directory.Move(stage,root);switched=true;
   if(integrate) undo=WindowsIntegration.Register(receipt);
   progress.Report((100,"Syphon is installed."));
   if(backed) { try { if(File.Exists(Path.Combine(backup,Marker))) RemoveOwnedFiles(backup,ReadReceiptForBackup(backup,root)); else if(!Directory.EnumerateFileSystemEntries(backup).Any())Directory.Delete(backup); } catch { /* Preserve the old copy if cleanup cannot finish. */ } }
   return receipt;
  } catch {
   undo?.Invoke();
   if(switched) { var failed=ReadReceipt(root);RemoveOwnedFiles(root,failed); }
   if(backed&&!Directory.Exists(root)) Directory.Move(backup,root);
   throw;
  } finally { if(Directory.Exists(stage)) DeleteStage(stage,parent); }
 }
 private static InstallReceipt ReadReceiptForBackup(string backup,string original) {
  var value=JsonSerializer.Deserialize<InstallReceipt>(File.ReadAllText(Path.Combine(backup,Marker)))!;
  if(value.Product!=Product||value.Root!=original)throw new IOException("Unrecognized backup.");return value;
 }
 private static void DeleteStage(string stage,string parent) {
  if(Path.GetDirectoryName(Path.GetFullPath(stage))!=Path.GetFullPath(parent)||!Path.GetFileName(stage).StartsWith(".Syphon-stage-",StringComparison.Ordinal)) throw new IOException("Invalid staging cleanup target.");
  NoLinks(stage);foreach(var file in Directory.EnumerateFiles(stage,"*",SearchOption.AllDirectories)) { NoLinks(Path.GetDirectoryName(file)!);if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Unexpected link in staging."); }
  Directory.Delete(stage,true);
 }
 internal static void RemoveOwnedFiles(string root,InstallReceipt receipt) {
  NoLinks(root);
  foreach(var relative in receipt.Files) {var file=SafePath(root,relative);NoLinks(Path.GetDirectoryName(file)!);if(File.Exists(file)) {if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Unexpected link in installation.");File.Delete(file);} }
  File.Delete(Path.Combine(root,Marker));
  // Only remove known empty directories, never an unknown user file or recursive tree.
  var dirs=receipt.Files.Select(f=>Path.GetDirectoryName(SafePath(root,f))).OfType<string>().SelectMany(d=>Ancestors(d,root)).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(p=>p.Length);
  foreach(var dir in dirs)if(Directory.Exists(dir)&&!Directory.EnumerateFileSystemEntries(dir).Any())Directory.Delete(dir);
  if(Directory.Exists(root)&&!Directory.EnumerateFileSystemEntries(root).Any())Directory.Delete(root);
 }
 private static IEnumerable<string> Ancestors(string path,string root) {for(var d=path;d.Length>root.Length;d=Path.GetDirectoryName(d)!)yield return d;}
 internal static void EnableBrowserIntegration(string root,bool integrate=true) {
  var receipt=ReadReceipt(root);if(receipt.Browser)return;
  var updated=receipt with { Browser=true };Action? undo=null;
  var pending=Path.Combine(root,Marker+".tmp");
  try {
   if(integrate)undo=WindowsIntegration.Register(updated);
   File.WriteAllText(pending,JsonSerializer.Serialize(updated));File.Move(pending,Path.Combine(root,Marker),true);
  }catch{undo?.Invoke();throw;}
  finally{if(File.Exists(pending))File.Delete(pending);}
 }
 internal static void Uninstall(string root,bool integrate=true) {root=Path.GetFullPath(root);var receipt=ReadReceipt(root);CheckRunning(root);NoLinks(root);if(integrate)WindowsIntegration.Remove(receipt);RemoveOwnedFiles(root,receipt);}
}
