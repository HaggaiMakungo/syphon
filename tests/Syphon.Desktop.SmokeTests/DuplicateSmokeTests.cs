using System.Windows;
using System.Windows.Controls;
using Syphon.Desktop;
using Syphon.Engine;
using Syphon.Core;
namespace Syphon.Desktop.SmokeTests;
internal static class DuplicateSmokeTests
{
 public static int Run() {
  var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  try {
   var task=new StoredDownload {Url="https://site/a",Destination=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"duplicate.mp3"),PartPath="unused",MediaFormat="mp3",State=DownloadState.Completed};
   if(DuplicateDownloadDialog.FindCandidate([task],task.Url,task.Destination,"mp4") is not null)throw new Exception("Different media outputs matched.");
   if(DuplicateDownloadDialog.FindCandidate([task],task.Url,task.Destination,"mp3")!=task)throw new Exception("Matching output was not found.");
   var dialog=new DuplicateDownloadDialog(task.Filename,true);
   if(dialog.FindName("DuplicateNumbered") is not RadioButton || dialog.FindName("DuplicateReplace") is not RadioButton || dialog.FindName("DuplicateExisting") is not RadioButton || dialog.FindName("RememberDuplicateChoice") is not CheckBox)throw new Exception("Duplicate choices missing.");
   if(dialog.Choice!="numbered")throw new Exception("Safe default missing.");dialog.Close();
   EngineChecks().GetAwaiter().GetResult();
   Console.WriteLine("PASS duplicate choices, media identity, receipt replay, safe replacement and recovery.");return 0;
  } finally {app.Shutdown();}
 }
 private static async Task EngineChecks() {
  var root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"Syphon.Duplicate.Tests",Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(root);
  var database=System.IO.Path.Combine(root,"store");
  string Digest(string p)=>Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(p)));
  try {
   await using(var c=await DownloadCoordinator.OpenAsync(database)) {
    var old=c.AcceptBrowserDownload(Guid.NewGuid(),new Uri("https://site/file"),root,filename:"file.bin",startImmediately:false).Task!;
    System.IO.File.WriteAllText(old.Destination,"old bytes");
    var set=typeof(DownloadCoordinator).GetMethod("Set",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
    old=old with {State=DownloadState.Completed,Sha256=Digest(old.Destination)};set.Invoke(c,[old]);
    var request=Guid.NewGuid();var reused=c.UseExistingBrowserDownload(request,old.Id,new Uri("https://other-site/file"));
    if(c.BrowserStatus(request)?.TaskId!=old.Id||!reused.Duplicate)throw new Exception("Existing receipt not persisted.");
    try{c.UseExistingBrowserDownload(request,old.Id,new Uri("https://wrong-site/file"));throw new Exception("Conflicting receipt accepted.");}catch(System.IO.InvalidDataException){}
    var fresh=c.AcceptBrowserDownload(Guid.NewGuid(),new Uri("https://site/file"),root,filename:"file.bin",startImmediately:false).Task!;
    c.ConfigureDuplicateReplacement(fresh.Id,old.Destination,old.Id);fresh=c.Get(fresh.Id);
    if(System.IO.File.ReadAllText(old.Destination)!="old bytes")throw new Exception("Original changed before success.");
    System.IO.File.WriteAllText(fresh.Destination,"fresh bytes");fresh=fresh with {State=DownloadState.Completed,Sha256=Digest(fresh.Destination)};set.Invoke(c,[fresh]);
    System.IO.File.WriteAllText(old.Destination,"modified");
    try{c.FinalizeDuplicateReplacement(fresh.Id);throw new Exception("Modified original overwritten.");}catch(System.IO.IOException){}
    if(System.IO.File.ReadAllText(fresh.Destination)!="fresh bytes")throw new Exception("Fresh file discarded on failure.");
    System.IO.File.WriteAllText(old.Destination,"old bytes");
    set.Invoke(c,[old with {State=DownloadState.Paused}]);
    try{c.FinalizeDuplicateReplacement(fresh.Id);throw new Exception("Changed old task ownership accepted.");}catch(System.IO.IOException){}
    if(System.IO.File.ReadAllText(old.Destination)!="old bytes")throw new Exception("Changed owner file overwritten.");
    set.Invoke(c,[old]);
    var blocker=c.AcceptBrowserDownload(Guid.NewGuid(),new Uri("https://site/blocker"),root,filename:"blocker.bin",startImmediately:false).Task!;
    set.Invoke(c,[blocker with {Destination=old.Destination}]);
    try{c.FinalizeDuplicateReplacement(fresh.Id);throw new Exception("Concurrent target owner accepted.");}catch(System.IO.IOException){}
    set.Invoke(c,[blocker]);
    using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
    var boundary=typeof(DownloadCoordinator).GetProperty("Boundary",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
    boundary.SetValue(c,(Action<string,StoredDownload>)((name,_)=>{if(name=="BeforeReplacementVerification"){entered.Set();release.Wait(TimeSpan.FromSeconds(5));}}));
    var finalizing=Task.Run(()=>c.FinalizeDuplicateReplacement(fresh.Id));
    if(!entered.Wait(TimeSpan.FromSeconds(3)))throw new Exception("Replacement verification did not start.");
    try{if(!Task.Run(()=>c.GetTasks()).Wait(TimeSpan.FromSeconds(1)))throw new Exception("Replacement verification holds coordinator gate.");}finally{release.Set();}
    var replaced=await finalizing;boundary.SetValue(c,null);
    if(System.IO.File.ReadAllText(replaced.Destination)!="fresh bytes"||System.IO.File.ReadAllText(c.Get(old.Id).Destination)!="old bytes")throw new Exception("Atomic replacement lost bytes or history.");
    if(c.FinalizeDuplicateReplacement(fresh.Id).Destination!=replaced.Destination)throw new Exception("Replacement not idempotent.");
    // Simulate crash after atomic rename, before snapshot update.
    var pending=fresh with {ReplacementBackup=c.Get(old.Id).Destination};set.Invoke(c,[pending]);
   }
   await using(var reopened=await DownloadCoordinator.OpenAsync(database)) {
    foreach(var task in reopened.GetTasks().Where(t=>t.ReplacementTarget is not null))reopened.FinalizeDuplicateReplacement(task.Id);
    if(reopened.GetTasks().Any(t=>t.ReplacementTarget is not null))throw new Exception("Pending replacement recovery failed.");
   }
  } finally {System.IO.Directory.Delete(root,true);}
 }
}
