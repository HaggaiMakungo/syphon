using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Syphon.Core;
using Syphon.Engine;
using System.Security.Cryptography;

internal static class SizeDiscoveryTests
{
    public static async Task<int> RunAsync() {
        var root=Path.Combine(Path.GetTempPath(),"Syphon.Size.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var builder=WebApplication.CreateSlimBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");await using var app=builder.Build();
        const long length=4194304;var block=new byte[65536];Random.Shared.NextBytes(block);
        app.MapGet("/{mode}",async context=>{
            var mode=context.Request.RouteValues["mode"]!.ToString();var range=context.Request.Headers.Range.Count>0;
            if(mode is "exact" or "stalled" or "changed" or "malformed")context.Response.Headers.ETag=range&&mode=="changed"?"\"v2\"":"\"v1\"";
            if(range&&mode=="timeout"){try{await Task.Delay(10000,context.RequestAborted);}catch(OperationCanceledException){}return;}
            if(range&&mode!="ignored"){
                await Task.Delay(250,context.RequestAborted);context.Response.StatusCode=206;
                context.Response.Headers.ContentRange=$"bytes {(mode=="malformed"?1:0)}-0/{length}";context.Response.ContentLength=1;await context.Response.Body.WriteAsync(block.AsMemory(0,1),context.RequestAborted);return;
            }
            context.Response.ContentType="video/mp2t";await context.Response.StartAsync(context.RequestAborted);
            try{for(var written=0L;written<length;written+=block.Length){await context.Response.Body.WriteAsync(block,context.RequestAborted);await context.Response.Body.FlushAsync(context.RequestAborted);await Task.Delay(mode=="stalled"&&written==0?900:25,context.RequestAborted);}}catch(OperationCanceledException){}
        });
        await app.StartAsync();var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
        async Task Wait(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(8);while(!condition()){if(DateTime.UtcNow>end)throw new Exception("Size discovery condition timed out.");await Task.Delay(20);}}
        try{
            foreach(var mode in new[]{"exact","stalled","estimate","changed","malformed","ignored","timeout"}){
                var folder=Path.Combine(root,mode);await using var owner=await DownloadCoordinator.OpenAsync(folder);var task=owner.Add(new(address+"/"+mode),Path.Combine(folder,"transfer.ts"),singleConnection:true);owner.Resume(task.Id);
                if(mode=="stalled")await Wait(()=>owner.Get(task.Id).Total==length&&owner.Get(task.Id).Received==0&&owner.Get(task.Id).State==DownloadState.Downloading);
                await Wait(()=>owner.Get(task.Id).Received>0);Check(owner.Get(task.Id).State==DownloadState.Downloading,"Metadata prevented the download starting.");
                if(mode=="exact")await Wait(()=>owner.Get(task.Id).Total==length&&owner.Get(task.Id).State==DownloadState.Downloading);
                if(mode=="estimate"){await Wait(()=>owner.Get(task.Id).EstimatedTotal==length);Check(owner.Get(task.Id).Total is null,"An unverified estimate became a confirmed total.");}
                if(mode is "changed" or "malformed"){await Task.Delay(450);Check(owner.Get(task.Id).Total is null&&owner.Get(task.Id).EstimatedTotal is null,"Incompatible metadata was accepted.");}
                await owner.WaitForIdleAsync(task.Id);var result=owner.Get(task.Id);Check(result.State==DownloadState.Completed,result.Message);Check(result.Total==length&&result.EstimatedTotal is null,"Final bytes did not replace size discovery.");
                using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);for(var n=0;n<length/block.Length;n++)hash.AppendData(block);Check(result.Sha256==Convert.ToHexStringLower(hash.GetHashAndReset()),"Published bytes changed.");Console.WriteLine("PASS background size discovery: "+mode);
            }
            await using(var owner=await DownloadCoordinator.OpenAsync(Path.Combine(root,"cancel"))){var task=owner.Add(new(address+"/timeout"),Path.Combine(root,"cancel.ts"),singleConnection:true);owner.Resume(task.Id);await Wait(()=>owner.Get(task.Id).Received>0);var watch=System.Diagnostics.Stopwatch.StartNew();await owner.PauseAsync(task.Id);Check(watch.Elapsed<TimeSpan.FromSeconds(1)&&owner.Get(task.Id).State==DownloadState.Paused,"Optional discovery delayed Pause.");Console.WriteLine("PASS metadata discovery cancels promptly with Pause");}
            return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{await app.StopAsync();Directory.Delete(root,true);}
    }
}
