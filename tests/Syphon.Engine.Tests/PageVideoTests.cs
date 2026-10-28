using Syphon.Core;
using Syphon.Engine;
internal static class PageVideoTests {
 internal static async Task<int> RunAsync(){
  static void Check(bool value,string message){if(!value)throw new Exception(message);}
  var folder=Path.Combine(Path.GetTempPath(),"Syphon.PageVideo",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  const string url="https://www.facebook.com/share/v/14jhjr6cQGm/";
  const string info="""{"title":"Public reel title","duration":12,"live_status":"not_live","availability":"public","formats":[{"format_id":"hd","width":720,"height":1280,"vcodec":"h264","acodec":"aac","filesize":4096},{"format_id":"encrypted","width":1080,"height":1920,"vcodec":"h264","acodec":"aac","has_drm":true}]}""";
  try{
   Check(PageVideoTools.IsKnownVideoPage(new(url)),"Facebook share link not recognized");Check(PageVideoTools.IsKnownVideoPage(new("https://vimeo.com/12345")),"Other extractor site not recognized");Check(!PageVideoTools.IsKnownVideoPage(new("https://facebook.com.evil.test/reel/123")),"Deceptive hostname accepted");
   Check(!PageVideoTools.IsKnownVideoPage(new("https://vimeo.com/files/video.mp4")),"Provider direct files must remain direct files");Check(!PageVideoTools.IsPageCandidate(new("https://cdn.test/video.mp4?token=1"))&&!PageVideoTools.IsPageCandidate(new("https://cdn.test/master.m3u8")),"Direct media must retain its stream path");Check(PageVideoTools.IsPageCandidate(new("https://example.test/watch/episode")),"Generic video page fallback unavailable");
   YoutubeTools.TestRunner=async(args,progress,token)=>{
    Check(!args.Contains("--cookies-from-browser")&&!args.Contains("--cookies"),"Browser credentials imported");Check(args.Contains("--ignore-config")&&args.Contains("--no-plugin-dirs")&&args.Contains("--no-remote-components"),"Extractor isolation lost");Check(!args.Contains("youtube"),"Generic page restricted to YouTube");Check(args[^1]==url,"Page address changed");
    if(args.Contains("--print"))return info;
    Check(args[Array.IndexOf(args,"--match-filters")+1].Contains("!=?is_live"),"Sites without live_status must not be silently skipped");Check(args.Contains("--no-playlist")&&args.Contains("--no-overwrites"),"Single-video publication safeguards lost");await File.WriteAllBytesAsync(args[Array.IndexOf(args,"--output")+1].Replace("%(ext)s","mp4"),[1,2,3,4],token);progress?.Invoke(4);return "";
   };
   var media=await MediaTools.InspectAsync(new(url));Check(media.Title=="Public reel title"&&media.Qualities.Length==1,"Public formats/title not extracted or DRM format exposed");Check(media.Qualities[0].QualityHeight==720,"Portrait resolution mislabeled");
   await using var owner=await DownloadCoordinator.OpenAsync(Path.Combine(folder,"store"));var id=owner.AcceptBrowserDownload(Guid.NewGuid(),new(url),folder,startImmediately:false).TaskId;Check(owner.Get(id).MediaFormat=="mp4","Facebook page queued as an HTML file");owner.Resume(id);await owner.WaitForIdleAsync(id);var done=owner.Get(id);Check(done.State==DownloadState.Completed&&done.Total==4&&done.Filename=="Public reel title.mp4",done.Message??"Page media did not finish");
   var unknown=new StoredDownload{Id=Guid.NewGuid(),Url="https://example.test/watch",Destination=Path.Combine(folder,"generic.mp4"),PartPath=Path.Combine(folder,"generic.part"),MediaFormat="mp4"};Check(PageVideoTools.RequiresExtractor(unknown),"Explicit video page mode not routed to extractor");
   Console.WriteLine("PASS Facebook and generic page routing, portrait qualities, DRM filtering, credential isolation, title naming and verified media publication");return 0;
  }finally{YoutubeTools.TestRunner=null;}
 }
 internal static async Task<int> LiveAsync(string[] args){
  var url=new Uri(args[0]);var output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
  var info=await MediaTools.InspectAsync(url);Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{info.Title,info.Duration,qualities=info.Qualities.Select(q=>new{q.YoutubeFormat,q.QualityHeight,q.Codec})}));
  await using var owner=await DownloadCoordinator.OpenAsync(Path.Combine(output,"store"));owner.Configure(new TransferOptions{MaximumRetries=0});var accepted=owner.AcceptBrowserDownload(Guid.NewGuid(),url,output,"mp4");
  var idle=owner.WaitForIdleAsync(accepted.TaskId);var expires=DateTime.UtcNow.AddMinutes(8);while(!idle.IsCompleted){if(DateTime.UtcNow>expires)throw new TimeoutException("Live download exceeded eight minutes");await Task.WhenAny(idle,Task.Delay(5000));var current=owner.Get(accepted.TaskId);Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{state=current.State.ToString(),current.Received,current.Message}));}await idle;var done=owner.Get(accepted.TaskId);
  if(done.State!=DownloadState.Completed){Console.WriteLine("FAIL "+done.Message+" "+done.Diagnostic);return 1;}
  Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{passed=true,done.Filename,done.Received,done.Sha256,done.Destination}));return 0;
 }

}
