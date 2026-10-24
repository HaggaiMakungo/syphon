using System.IO;
using Microsoft.Win32;
using Syphon.Core;
using Syphon.Engine;
namespace Syphon.Desktop;
public partial class MainWindow
{
 private (string Target,Guid? OldTaskId)? pendingDuplicateReplacement;
 private bool HasPendingDuplicateReplacement => pendingDuplicateReplacement is not null;
 private bool TryExistingDownload(string source,string folder,string filename,bool immediately,out BrowserAcceptance? result,string? mediaFormat=null) {
  result=null;pendingDuplicateReplacement=null;if(coordinator is null)return false;
  if(mediaFormat is null&&Uri.TryCreate(source,UriKind.Absolute,out var uri)&&YoutubeTools.IsYoutubeVideoTarget(uri))mediaFormat="mp4";
  var target=Path.Combine(folder,filename);
  if(UsesCategoryFolders(folder))target=Path.Combine(DownloadCategories.Resolve(folder,filename,mediaFormat),filename);
  var old=DuplicateDownloadDialog.FindCandidate(coordinator.GetTasks(),source,target,mediaFormat);
  if(old is null&&!File.Exists(target))return false;
  var preferences=DownloadPreferences.Load(preferencesPath);var choice=preferences.DuplicateChoice;
  var canReplace=old is null||old.State==DownloadState.Completed&&string.Equals(Path.GetDirectoryName(old.Destination),Path.GetDirectoryName(target),StringComparison.OrdinalIgnoreCase);
  if(choice=="ask"||choice=="replace"&&!canReplace) {
   var dialog=new DuplicateDownloadDialog(old?.Filename??filename,canReplace){Owner=this};
   if(dialog.ShowDialog()!=true)throw new OperationCanceledException("Download cancelled.");
   choice=dialog.Choice;if(dialog.Remember)(preferences with {DuplicateChoice=choice}).Save(preferencesPath);
  }
  if(choice=="numbered")return false;
  if(choice=="replace") {pendingDuplicateReplacement=(old?.Destination??target,old?.Id);return false;}
  if(old is null) {
   var dialog=new CompletionWindow(new StoredDownload {Url=source,Destination=target,PartPath="",State=DownloadState.Completed}){Owner=this};dialog.Show();
   throw new OperationCanceledException("Existing file opened. No download added.");
  }
  if(old.State==DownloadState.Completed&&!File.Exists(old.Destination)) {
   var answer=System.Windows.MessageBox.Show(this,"The existing file is missing. Choose Yes to locate it, No to download a fresh copy, or Cancel.","Existing file missing",System.Windows.MessageBoxButton.YesNoCancel);
   if(answer==System.Windows.MessageBoxResult.No)return false;
   if(answer!=System.Windows.MessageBoxResult.Yes)throw new OperationCanceledException("Download cancelled.");
   var picker=new OpenFileDialog {Title="Locate existing download",FileName=old.Filename};
   if(picker.ShowDialog(this)!=true)throw new OperationCanceledException("Download cancelled.");
   old=coordinator.LocateCompletedDownload(old.Id,picker.FileName);ShowCompletion(old);
  } else if(old.State==DownloadState.Completed)ShowCompletion(old);
  else if(immediately&&old.State is DownloadState.Paused or DownloadState.Failed or DownloadState.Cancelled)coordinator.Resume(old.Id);
  result=new(old.Id,coordinator.Get(old.Id),true,false);return true;
 }
 private void RegisterDuplicateReplacement(StoredDownload task,bool immediately=true) {
  var replacement=pendingDuplicateReplacement;pendingDuplicateReplacement=null;if(replacement is null)return;
  coordinator!.ConfigureDuplicateReplacement(task.Id,replacement.Value.Target,replacement.Value.OldTaskId);
  if(immediately)coordinator.Resume(task.Id);
 }
 private void RecoverDuplicateReplacements() {
  foreach(var task in coordinator!.GetTasks().Where(t=>t.State==DownloadState.Completed&&t.ReplacementTarget is not null)) {
   try {CompleteDuplicateReplacement(task);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){commandError=ex.Message;}
  }
 }
 private StoredDownload CompleteDuplicateReplacement(StoredDownload task) => coordinator!.FinalizeDuplicateReplacement(task.Id);
}
