using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Syphon.Desktop;
namespace Syphon.Desktop.SmokeTests;

internal static class AutomaticUpdateTests
{
    public static int Signature(string path)
    {
        var copy=Path.Combine(Path.GetTempPath(),"Syphon.Update.Signature-"+Guid.NewGuid().ToString("N")+".exe");
        try {
            InstallerSignature.VerifyPackage(path,"0.1.0-beta.18");
            try { InstallerSignature.VerifyPackage(path,"0.1.0-beta.19"); throw new Exception("Wrong signed version accepted."); } catch(InvalidDataException){}
            File.Copy(path,copy);
            using(var stream=new FileStream(copy,FileMode.Open,FileAccess.ReadWrite)) {stream.Position=128;var original=stream.ReadByte();stream.Position=128;stream.WriteByte((byte)(original^1));}
            try { InstallerSignature.Verify(copy);throw new Exception("Tampered signed installer accepted."); } catch(InvalidDataException){} catch(CryptographicException){}
            Console.WriteLine("PASS real update signature: expected publisher, signed version, and tampered-file rejection.");return 0;
        } catch(Exception ex){Console.Error.WriteLine(ex);return 1;} finally {if(File.Exists(copy))File.Delete(copy);}
    }
    public static async Task<int> Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "Syphon.Update.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var bytes = "verified installer fixture"u8.ToArray(); var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            string Release(string assetUrl, string digest) => JsonSerializer.Serialize(new[] { new { tag_name="v0.1.0-beta.19", html_url="https://github.com/HaggaiMakungo/syphon/releases/tag/v0.1.0-beta.19", draft=false, prerelease=true, body="notes", assets=new[]{new{name="Syphon-Setup-0.1.0-beta.19-win-x64.exe", browser_download_url=assetUrl, size=bytes.Length, digest}} } });
            const string url="https://github.com/HaggaiMakungo/syphon/releases/download/v0.1.0-beta.19/Syphon-Setup-0.1.0-beta.19-win-x64.exe";
            var release=ReleaseUpdates.SelectRelease(Release(url,"sha256:"+hash),"0.1.0-beta.18",null)!;
            if(release.Installer is null)throw new Exception("Matching installer asset was not selected.");
            if(ReleaseUpdates.SelectRelease(Release("https://evil.example/update.exe","sha256:"+hash),"0.1.0-beta.18",null)!.Installer is not null)throw new Exception("Untrusted installer URL selected.");
            if(ReleaseUpdates.SelectRelease(Release(url,"missing"),"0.1.0-beta.18",null)!.Installer is not null)throw new Exception("Missing release digest was accepted.");
            var type=typeof(MainWindow).Assembly.GetType("Syphon.Desktop.BackgroundUpdater")??throw new Exception("Background downloader is missing.");
            var method=type.GetMethod("DownloadAsync")!;
            async Task<string> Download(byte[] body, UpdatePackage package, Action<string> verify, CancellationToken token=default) {
                using var client=new HttpClient(new FixtureHandler(body));
                var operation=(Task<string>)method.Invoke(null,[package,root,new Progress<(long,long?)>(),token,client,verify])!;
                return await operation;
            }
            var verified=false;
            var file=await Download(bytes,release.Installer,_=>verified=true);
            if(!verified||!File.ReadAllBytes(file).SequenceEqual(bytes))throw new Exception("Verified download did not publish exact bytes.");
            try { await Download(bytes,release.Installer with{Sha256=new string('0',64)},_=>{});throw new Exception("Corrupt installer was accepted."); } catch(InvalidDataException){}
            try { await Download(bytes[..^1],release.Installer,_=>{});throw new Exception("Truncated installer was accepted."); } catch(InvalidDataException){}
            using var cancel=new CancellationTokenSource();cancel.Cancel();
            try { await Download(bytes,release.Installer,_=>{},cancel.Token);throw new Exception("Cancelled download succeeded."); } catch(OperationCanceledException){}
            try { await Download(bytes,release.Installer,_=>throw new InvalidDataException("wrong signer"));throw new Exception("Wrong publisher was accepted."); } catch(InvalidDataException){}
            using(var redirectClient=new HttpClient(new RedirectHandler())) {
                try {await (Task<string>)method.Invoke(null,[release.Installer,root,new Progress<(long,long?)>(),CancellationToken.None,redirectClient,(Action<string>)(_=>{})])!;throw new Exception("Untrusted redirect accepted.");}catch(InvalidDataException){}
            }
            if(Directory.GetFiles(root,"*.part",SearchOption.AllDirectories).Length!=0)throw new Exception("Failed download left temporary installer.");
            var ticketPath=Path.Combine(root,"resume.json");var id=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
            new UpdateResumeTicket("0.1.0-beta.19",now,[id,id]).Save(ticketPath);
            if(UpdateResumeTicket.Consume(ticketPath,"0.1.0-beta.19",now).Single()!=id||File.Exists(ticketPath))throw new Exception("Update resume ticket was not consumed once.");
            new UpdateResumeTicket("0.1.0-beta.19",now,[id]).Save(ticketPath);
            if(UpdateResumeTicket.Consume(ticketPath,"0.1.0-beta.18",now).Length!=0)throw new Exception("Old version resumed tasks after a failed upgrade.");
            new UpdateResumeTicket("0.1.0-beta.19",now.AddDays(-2),[id]).Save(ticketPath);
            if(UpdateResumeTicket.Consume(ticketPath,"0.1.0-beta.19",now).Length!=0)throw new Exception("Expired update intent resumed tasks.");
            Console.WriteLine("PASS automatic updates: release asset origin/digest, exact download, truncation/hash/signer rejection, cancellation and cleanup.");return 0;
        } catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally {Directory.Delete(root,true);}
    }
    private sealed class FixtureHandler(byte[] body):HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(body),RequestMessage=request});}
    }
    private sealed class RedirectHandler:HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
            var response=new HttpResponseMessage(HttpStatusCode.Redirect);response.Headers.Location=new Uri("https://evil.example/installer.exe");return Task.FromResult(response);
        }
    }
}
