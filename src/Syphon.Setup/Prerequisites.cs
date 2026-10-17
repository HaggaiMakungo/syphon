using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace Syphon.Setup;
internal static class Prerequisites
{
 internal static void EnsureWebView() {
  if(WindowsIntegration.WebViewPresent())return;
  var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Syphon-WebView-"+Guid.NewGuid().ToString("N"));InstallEngine.NoLinks(dir);Directory.CreateDirectory(dir);var file=Path.Combine(dir,"MicrosoftEdgeWebview2Setup.exe");
  try {
   using(var stream=InstallEngine.OpenPayload())using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)) {
    using var json=zip.GetEntry("release-manifest.json")!.Open();var manifest=JsonSerializer.Deserialize<PackageManifest>(json)!;
    const string name="prerequisites/MicrosoftEdgeWebview2Setup.exe";
    var expected=manifest.Files[name];using(var input=zip.GetEntry(name)!.Open())using(var output=File.Create(file))input.CopyTo(output);
    using var check=File.OpenRead(file);if(check.Length!=expected.Length||!Convert.ToHexString(SHA256.HashData(check)).Equals(expected.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Microsoft WebView2 installer failed verification.");
   }
   VerifySignature(file);
   using var process=Process.Start(new ProcessStartInfo(file,"/silent /install"){UseShellExecute=false,CreateNoWindow=true})??throw new IOException("Microsoft WebView2 setup could not start.");
   if(!process.WaitForExit(600000))throw new IOException("Microsoft WebView2 is still installing. Wait for it to finish, then retry Syphon Setup.");
   if(!WindowsIntegration.WebViewPresent())throw new IOException("Microsoft WebView2 could not be installed. Check your internet connection and retry setup. Microsoft setup returned "+process.ExitCode+".");
  } finally { try{File.Delete(file);Directory.Delete(dir);}catch(IOException){} }
 }
 internal static void VerifySignature(string file) {
  var info=new TrustFile {Size=(uint)Marshal.SizeOf<TrustFile>(),Path=file};var ptr=Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
  try {
   Marshal.StructureToPtr(info,ptr,false);var data=new TrustData{Size=(uint)Marshal.SizeOf<TrustData>(),UI=2,Checks=1,Choice=1,File=ptr,Flags=0x80};var action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
   if(WinVerifyTrust(new IntPtr(-1),ref action,ref data)!=0)throw new IOException("Microsoft WebView2 setup does not have a trusted Windows signature.");
  } finally {Marshal.DestroyStructure<TrustFile>(ptr);Marshal.FreeHGlobal(ptr);}
 }
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct TrustFile{public uint Size;[MarshalAs(UnmanagedType.LPWStr)]public string Path;public IntPtr Handle,KnownSubject;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct TrustData{public uint Size;public IntPtr Callback,Sip;public uint UI,Checks,Choice;public IntPtr File;public uint StateAction;public IntPtr State,Url;public uint Flags,Context;}
 [DllImport("wintrust.dll",ExactSpelling=true,PreserveSig=true)]private static extern int WinVerifyTrust(IntPtr hwnd,ref Guid action,ref TrustData data);
}
