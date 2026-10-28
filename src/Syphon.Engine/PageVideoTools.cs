using System.Security.Cryptography;
using System.Text;
using Syphon.Core;
namespace Syphon.Engine;
/// <summary>Page extraction is explicit for unknown sites; ordinary file URLs stay file downloads.</summary>
public static class PageVideoTools {
 public static bool IsKnownVideoPage(Uri url){
  if(YoutubeTools.IsYoutubeVideoTarget(url))return true;
  if(!IsPageCandidate(url))return false;
  var host=url.IdnHost.ToLowerInvariant();var path=url.AbsolutePath.ToLowerInvariant();
  bool Site(string domain)=>host==domain||host.EndsWith("."+domain,StringComparison.Ordinal);
  if(Site("facebook.com"))return path.StartsWith("/reel/")||path.StartsWith("/share/v/")||path.StartsWith("/share/r/")||path.Contains("/videos/")||path=="/watch"||path=="/watch/"||path.EndsWith("/video.php");
  if(Site("fb.watch"))return path.Length>1;
  return path.Length>1&&new[]{"vimeo.com","instagram.com","tiktok.com","twitter.com","x.com","dailymotion.com","dai.ly","reddit.com","redd.it","twitch.tv","pinterest.com","pin.it","bilibili.com","b23.tv","rumble.com","streamable.com"}.Any(Site);
 }
 public static bool IsPageCandidate(Uri url)=>url.Scheme is "http" or "https"&&!YoutubeTools.IsCaptionUrl(url)&&!MediaTools.IsManifestUrl(url.AbsoluteUri)&&!new[]{".mp4",".webm",".mkv",".mov",".avi",".flv",".m4v",".ts",".m2ts",".mp3",".m4a",".ogg",".wav",".flac",".aac",".opus",".vtt",".srt",".pdf",".zip",".7z",".rar",".jpg",".jpeg",".png",".gif",".webp",".exe",".msi",".docx",".xlsx",".pptx"}.Contains(Path.GetExtension(url.AbsolutePath).ToLowerInvariant());
 public static Uri CanonicalUrl(Uri url){HttpDownloadEngine.ValidateUrl(url);if(!string.IsNullOrEmpty(url.UserInfo))throw new ArgumentException("Use a link without embedded credentials.");return YoutubeTools.IsYoutubeVideoTarget(url)?YoutubeTools.CanonicalUrl(url):url;}
 public static bool RequiresExtractor(StoredDownload task)=>task.MediaFormat is not null&&(IsKnownVideoPage(new(task.Url))||task.YoutubeFormat is not null||task.VideoStream is null&&task.AudioStream is null&&IsPageCandidate(new(task.Url)));
 public static string PlaceholderStem(Uri url)=>YoutubeTools.IsYoutubeVideoTarget(url)?"youtube-"+YoutubeTools.VideoId(url):(url.IdnHost.EndsWith("facebook.com",StringComparison.OrdinalIgnoreCase)||url.IdnHost=="fb.watch"?"facebook-":"video-")+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri)))[..12];
}
