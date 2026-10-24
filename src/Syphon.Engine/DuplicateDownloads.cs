using System.Security.Cryptography;
using Syphon.Core;
namespace Syphon.Engine;
public sealed partial class DownloadCoordinator
{
 public BrowserAcceptance UseExistingBrowserDownload(Guid requestId, Guid taskId, Uri url, string? mediaFormat=null, int? videoStream=null, int? audioStream=null, string? youtubeFormat=null, int? qualityHeight=null) {
  lock(gate) {
   HttpDownloadEngine.ValidateUrl(url);
   if(YoutubeTools.IsYoutubeVideoTarget(url)){url=YoutubeTools.CanonicalUrl(url);mediaFormat??="mp4";}
   var hash=DownloadStore.BrowserIdentityHash(url.AbsoluteUri,mediaFormat,videoStream,audioStream,youtubeFormat,qualityHeight);
   if(store.FindBrowserRequest(requestId) is { } saved) {
    if(saved.UrlHash!=hash)throw new InvalidDataException("This request ID already belongs to a different link.");
    return BrowserStatus(requestId)!;
   }
   if(!tasks.ContainsKey(taskId))throw new InvalidOperationException("The existing task was removed.");
   store.RegisterExistingReceipt(requestId,taskId,hash);
   return new(taskId,tasks[taskId],true,false);
  }
 }
 public StoredDownload LocateCompletedDownload(Guid taskId,string path) {
  StoredDownload task;lock(gate){task=tasks[taskId];}
  path=Path.GetFullPath(path);
  if(task.State!=DownloadState.Completed||!File.Exists(path))throw new IOException("Choose an existing completed file.");
  var stamp=FileMetadataIdentity(path);
  if(task.Sha256 is not null&&FileDigest(path)!=task.Sha256)throw new IOException("This file does not match the recorded download checksum.");
  lock(gate) {
   if(tasks[taskId]!=task||!SameStamp(path,stamp))throw new IOException("The file changed while it was being checked.");
   Set(task with {Destination=path});return tasks[taskId];
  }
 }
 public void ConfigureDuplicateReplacement(Guid taskId,string target,Guid? oldTaskId) {
  lock(gate) {
   var task=tasks[taskId];target=Path.GetFullPath(target);
   if(workers.ContainsKey(taskId)||task.State!=DownloadState.Paused)throw new InvalidOperationException("Replacement must be configured before downloading.");
   if(!string.Equals(Path.GetDirectoryName(target),Path.GetDirectoryName(task.Destination),StringComparison.OrdinalIgnoreCase)||string.Equals(target,task.Destination,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Replacement must use a separate file in the same folder.");
   if(tasks.Values.Any(t=>t.Id!=oldTaskId&&t.Id!=taskId&&string.Equals(t.Destination,target,StringComparison.OrdinalIgnoreCase)&&t.State!=DownloadState.Completed))throw new InvalidOperationException("Another download is using the replacement file.");
   if(oldTaskId is Guid oldId&&(!tasks.TryGetValue(oldId,out var oldTask)||oldTask.State!=DownloadState.Completed))throw new InvalidOperationException("Only a completed existing download can be replaced.");
   var digest=File.Exists(target)?oldTaskId is Guid knownId&&tasks[knownId].Sha256 is string knownHash?knownHash:FileMetadataIdentity(target):null;
   Set(task with {ReplacementTarget=target,ReplacementOldTaskId=oldTaskId,ReplacementExpectedSha256=digest,ReplacementBackup=Path.Combine(Path.GetDirectoryName(target)!,Path.GetFileNameWithoutExtension(target)+$" (replaced {taskId:N})"+Path.GetExtension(target))});
  }
 }
 public StoredDownload FinalizeDuplicateReplacement(Guid taskId) {
  StoredDownload task;
  lock(gate) {task=tasks[taskId];if(task.State!=DownloadState.Completed||task.ReplacementTarget is null)return task;}
  var target=task.ReplacementTarget;var backup=task.ReplacementBackup!;
  if(task.Sha256 is null)throw new IOException("Replacement has no verified checksum. The original file was kept.");
  Boundary?.Invoke("BeforeReplacementVerification", task);
  // Large-file hashing never holds the coordinator gate: progress, pause and shutdown stay responsive.
  var stagedStamp=File.Exists(task.Destination)?FileMetadataIdentity(task.Destination):null;
  var targetStamp=File.Exists(target)?FileMetadataIdentity(target):null;
  var backupStamp=File.Exists(backup)?FileMetadataIdentity(backup):null;
  var staged=stagedStamp is not null&&FileDigest(task.Destination)==task.Sha256;
  var alreadyPublished=stagedStamp is null&&targetStamp is not null&&FileDigest(target)==task.Sha256&&(task.ReplacementExpectedSha256 is null||backupStamp is not null&&MatchesReplacementIdentity(backup,task.ReplacementExpectedSha256));
  if(!staged&&!alreadyPublished)throw new IOException("Replacement file changed. The original file was kept.");
  if(staged&&task.ReplacementExpectedSha256 is not null&&(targetStamp is null||!MatchesReplacementIdentity(target,task.ReplacementExpectedSha256)||backupStamp is not null))throw new IOException("The original file changed since confirmation. Both files were kept.");
  lock(gate) {
   if(tasks[taskId]!=task)throw new IOException("The replacement changed during verification. Both files were kept.");
   if(task.ReplacementOldTaskId is Guid ownerId&&tasks.TryGetValue(ownerId,out var owner)&&
      (owner.State!=DownloadState.Completed||workers.ContainsKey(ownerId)||!string.Equals(owner.Destination,target,StringComparison.OrdinalIgnoreCase)&&!alreadyPublished))throw new IOException("The existing download changed during replacement. Both files were kept.");
   if(tasks.Values.Any(t=>t.Id!=taskId&&t.Id!=task.ReplacementOldTaskId&&t.State!=DownloadState.Completed&&string.Equals(t.Destination,target,StringComparison.OrdinalIgnoreCase)))throw new IOException("Another download is using the original file. Both files were kept.");
   if(!SameStamp(task.Destination,stagedStamp)||!SameStamp(target,targetStamp)||!SameStamp(backup,backupStamp))throw new IOException("A file changed during replacement verification. Both files were kept.");
   if(staged) {
    if(task.ReplacementExpectedSha256 is null) {if(targetStamp is not null)throw new IOException("A new file appeared at the replacement location. Both files were kept.");File.Move(task.Destination,target,false);}
    else File.Replace(task.Destination,target,backup,true);
   }
   var updated=task with {Destination=target,ReplacementTarget=null,ReplacementBackup=null,ReplacementExpectedSha256=null,ReplacementOldTaskId=null,Message="Download complete. Replaced the previous file."};
   var changed=new List<StoredDownload>{updated};
   if(task.ReplacementOldTaskId is Guid oldId&&tasks.TryGetValue(oldId,out var old))changed.Add(old with {Destination=backup,Message=File.Exists(backup)?"Replaced by a fresh download. Previous bytes retained.":"Replaced by a fresh download. The previous file was already missing."});
   store.SaveMany(changed);foreach(var item in changed)tasks[item.Id]=item;
   return updated;
  }
 }
 private static bool SameStamp(string path,string? stamp) => stamp is null?!File.Exists(path):File.Exists(path)&&FileMetadataIdentity(path)==stamp;
 private static string FileMetadataIdentity(string path) {var file=new FileInfo(path);return $"metadata:{file.Length}:{file.LastWriteTimeUtc.Ticks}";}
 private static bool MatchesReplacementIdentity(string path,string identity) => identity.StartsWith("metadata:",StringComparison.Ordinal)?FileMetadataIdentity(path)==identity:FileDigest(path)==identity;
 private static string FileDigest(string path) {using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);return Convert.ToHexStringLower(SHA256.HashData(stream));}
}
