using System.IO;
using System.Text.Json;
namespace Syphon.Setup;
internal sealed record BrowserDistribution(string? ChromeExtensionId=null,string? EdgeExtensionId=null)
{
 internal static BrowserDistribution Load(string root) {
  var path=Path.Combine(root,"browser-distribution.json");
  var value=File.Exists(path)?JsonSerializer.Deserialize<BrowserDistribution>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??new():new();
  foreach(var id in new[]{value.ChromeExtensionId,value.EdgeExtensionId})if(id is not null&&(id.Length!=32||id.Any(c=>c<'a'||c>'p')))throw new InvalidDataException("Browser store extension IDs must contain exactly 32 letters from a to p.");
  return value;
 }
 internal string? StoreUrl(bool edge) {
  var id=edge?EdgeExtensionId:ChromeExtensionId;
  return id is null?null:(edge?"https://microsoftedge.microsoft.com/addons/detail/":"https://chromewebstore.google.com/detail/")+id;
 }
 internal string[] AllowedOrigins(string developmentId)=>new[]{developmentId,ChromeExtensionId,EdgeExtensionId}.OfType<string>().Distinct(StringComparer.Ordinal).Select(id=>"chrome-extension://"+id+"/").ToArray();
}
