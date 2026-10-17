using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Syphon.Core;
using Syphon.Engine;
using Syphon.TestServer;

internal static class RateScheduleTests
{
    public static async Task<int> StandaloneAsync(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(),"Syphon.Speed.Tests",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var results = new List<object>(); int failed = 0;
        await using var server = await FixtureServer.StartAsync();
        try { await RunAsync(root,server,async(name,action)=>{try{await action();results.Add(new{name,passed=true});Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;results.Add(new{name,passed=false,error=ex.ToString()});Console.WriteLine("FAIL "+name+": "+ex);}}); }
        finally { if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(),"Syphon.Speed.Tests"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new Exception("Invalid test cleanup path."); Directory.Delete(root,true); }
        var index=Array.IndexOf(args,"--report");if(index>=0&&index+1<args.Length){var path=Path.GetFullPath(args[index+1]);Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{failed,results},new JsonSerializerOptions{WriteIndented=true}));}
        Console.WriteLine($"{results.Count-failed}/{results.Count} speed and schedule checks passed");return failed==0?0:1;
    }
    public static async Task RunAsync(string root,FixtureServer server,Func<string,Func<Task>,Task> run)
    {
        string Folder(){var path=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
        Uri Url(string route)=>new(server.BaseAddress,route);
        static void Check(bool v,string message){if(!v)throw new InvalidOperationException(message);}
        static async Task Until(Func<bool> p){var end=DateTime.UtcNow.AddSeconds(20);while(!p()){if(DateTime.UtcNow>end)throw new TimeoutException("Speed/schedule fixture timed out.");await Task.Delay(10);}}
        static async Task Verify(StoredDownload task,long length){Check(task.State==DownloadState.Completed,task.Message);await using var input=File.OpenRead(task.Destination);Check(input.Length==length&&Convert.ToHexStringLower(await SHA256.HashDataAsync(input))==Payload.ExpectedHash(length),"Throttling changed saved bytes.");}
        var defaults = new TransferOptions { SegmentationThreshold=65536, MaximumRetries=0 };

        await run("Global cap is shared by concurrent files and all range workers",async()=>{
            var folder=Folder();await using var owner=await DownloadCoordinator.OpenAsync(folder,defaults with { GlobalBytesPerSecond=524288 });
            var jobs=Enumerable.Range(0,2).Select(i=>owner.Add(Url("segments?size=524288"),Path.Combine(folder,$"{i}.bin"))).ToArray();
            var clock=Stopwatch.StartNew();foreach(var job in jobs)owner.Resume(job.Id);
            await Task.WhenAll(jobs.Select(j=>owner.WaitForIdleAsync(j.Id)));
            Check(clock.Elapsed.TotalSeconds>=1.75,$"Aggregate cap multiplied by connections: {clock.Elapsed.TotalSeconds:F2}s.");
            foreach(var job in jobs){await Verify(owner.Get(job.Id),524288);Check(owner.Get(job.Id).Segments.Length==4,"Fixture did not use segmented workers.");}
        });
        await run("A per-file cap does not block an uncapped file and survives reopen",async()=>{
            var folder=Folder();Guid slowId;
            await using(var owner=await DownloadCoordinator.OpenAsync(folder,defaults)){
                var slow=owner.Add(Url("segments?size=1048576"),Path.Combine(folder,"slow.bin"));slowId=slow.Id;owner.SetSpeedLimit(slow.Id,262144);
                var fast=owner.Add(Url("segments?size=1048576"),Path.Combine(folder,"fast.bin"));
                var clock=Stopwatch.StartNew();owner.Resume(slow.Id);owner.Resume(fast.Id);await owner.WaitForIdleAsync(fast.Id);
                Check(owner.Get(slow.Id).State!=DownloadState.Completed,"Per-file limit blocked the other job or was not enforced.");
                await owner.WaitForIdleAsync(slow.Id);Check(clock.Elapsed.TotalSeconds>=3.6,"Per-file rate multiplied by range count.");
                await Verify(owner.Get(slow.Id),1048576);await Verify(owner.Get(fast.Id),1048576);
            }
            await using var reopened=await DownloadCoordinator.OpenAsync(folder,defaults);Check(reopened.Get(slowId).SpeedLimitBytesPerSecond==262144,"Per-file speed cap was not durable.");
        });
        await run("Speed changes apply live and bandwidth waiting is not a server idle timeout",async()=>{
            var folder=Folder();await using var owner=await DownloadCoordinator.OpenAsync(folder,defaults with { BodyIdleTimeout=TimeSpan.FromMilliseconds(100), GlobalBytesPerSecond=1024 });
            var task=owner.Add(Url("file?size=262144"),Path.Combine(folder,"live.bin"),true);owner.Resume(task.Id);
            await Until(()=>owner.Get(task.Id).State==DownloadState.Downloading);await Task.Delay(250);
            Check(owner.Get(task.Id).RetryCount==0&&owner.Get(task.Id).State==DownloadState.Downloading,"Intentional throttling was treated as a network timeout.");
            owner.Configure(owner.Options with { GlobalBytesPerSecond=0 });await owner.WaitForIdleAsync(task.Id).WaitAsync(TimeSpan.FromSeconds(5));await Verify(owner.Get(task.Id),262144);
        });
        await run("Pause cancels bandwidth waits promptly and resume retains exact bytes",async()=>{
            var folder=Folder();await using var owner=await DownloadCoordinator.OpenAsync(folder,defaults);
            var task=owner.Add(Url("segments?size=262144"),Path.Combine(folder,"pause.bin"));owner.SetSpeedLimit(task.Id,1024);owner.Resume(task.Id);
            await Until(()=>owner.Get(task.Id).State==DownloadState.Downloading);var clock=Stopwatch.StartNew();await owner.PauseAsync(task.Id);
            Check(clock.Elapsed<TimeSpan.FromSeconds(1)&&owner.Get(task.Id).State==DownloadState.Paused,"Pause did not interrupt the limiter.");
            owner.SetSpeedLimit(task.Id,0);owner.Resume(task.Id);await owner.WaitForIdleAsync(task.Id);await Verify(owner.Get(task.Id),262144);
        });
        await run("Rate and schedule validation preserves current settings on rejection",async()=>{
            var folder=Folder();await using var owner=await DownloadCoordinator.OpenAsync(folder,defaults);
            foreach(var invalid in new[]{-1L,1L,1073741825L}){try{owner.Configure(defaults with {GlobalBytesPerSecond=invalid});throw new Exception("Invalid rate accepted.");}catch(ArgumentOutOfRangeException){}}
            try{owner.Configure(defaults with {Schedule=new(true,60,60)});throw new Exception("Zero-width window accepted.");}catch(ArgumentException){}
            Check(owner.Options==defaults,"Rejected settings changed active controls.");
        });
        await run("Daily and overnight windows use inclusive start and exclusive stop",()=>{
            var day=new QueueSchedule(true,540,600);var night=new QueueSchedule(true,1320,360);
            DateTimeOffset At(int hour,int minute=0)=>new(2026,10,16,hour,minute,0,TimeSpan.Zero);
            Check(!day.IsOpen(At(8,59))&&day.IsOpen(At(9))&&!day.IsOpen(At(10)),"Day boundary mismatch.");
            Check(night.IsOpen(At(22))&&night.IsOpen(At(3))&&!night.IsOpen(At(6))&&!night.IsOpen(At(21,59)),"Overnight boundary mismatch.");return Task.CompletedTask;
        });
        await run("Schedule gates admission, pauses on stop, and respects manual pause within a window",async()=>{
            var folder=Folder();var time=new ManualClock(new(2026,10,16,8,0,0,TimeSpan.Zero));
            var options=defaults with {GlobalBytesPerSecond=1048576,Schedule=new(true,540,600)};
            await using var owner=await DownloadCoordinator.OpenAsync(folder,options,time);
            var task=owner.Add(Url("segments?size=8388608"),Path.Combine(folder,"scheduled.bin"));owner.Resume(task.Id);
            await Task.Delay(80);Check(owner.Get(task.Id).State==DownloadState.Queued&&owner.Get(task.Id).Received==0,"Closed schedule contacted the server.");
            time.Now=time.Now.AddHours(1);await owner.RefreshScheduleAsync();await Until(()=>owner.Get(task.Id).Received>0);
            await owner.PauseAsync(task.Id);await owner.RefreshScheduleAsync();Check(owner.Get(task.Id).State==DownloadState.Paused,"Schedule undid manual pause.");
            owner.Resume(task.Id);await Until(()=>owner.Get(task.Id).State==DownloadState.Downloading);
            time.Now=time.Now.AddHours(1);await owner.RefreshScheduleAsync();Check(owner.Get(task.Id).State==DownloadState.Paused,"Scheduled stop left a worker active.");
            owner.Resume(task.Id);Check(owner.Get(task.Id).State==DownloadState.Queued,"Manual resume bypassed the schedule.");
            time.Now=time.Now.AddHours(23);await owner.RefreshScheduleAsync();await Until(()=>owner.Get(task.Id).State==DownloadState.Downloading);
            await owner.PauseAsync(task.Id);
        });
        await run("Enabled schedule resumes saved queue only when reopened inside its window",async()=>{
            var folder=Folder();Guid id;var time=new ManualClock(new(2026,10,16,8,0,0,TimeSpan.Zero));var options=defaults with {Schedule=new(true,540,600)};
            await using(var owner=await DownloadCoordinator.OpenAsync(folder,options,time)){id=owner.Add(Url("resume?size=8388608"),Path.Combine(folder,"reopen.bin")).Id;}
            await using(var closed=await DownloadCoordinator.OpenAsync(folder,options,time)){Check(closed.Get(id).State==DownloadState.Paused,"Closed window auto-started saved work.");}
            time.Now=time.Now.AddHours(1);await using var opened=await DownloadCoordinator.OpenAsync(folder,options,time);await Until(()=>opened.Get(id).Received>0);await opened.PauseAsync(id);
        });
    }
    private sealed class ManualClock(DateTimeOffset now):TimeProvider
    {public DateTimeOffset Now=now;public override DateTimeOffset GetUtcNow()=>Now;public override TimeZoneInfo LocalTimeZone=>TimeZoneInfo.Utc;}
}
