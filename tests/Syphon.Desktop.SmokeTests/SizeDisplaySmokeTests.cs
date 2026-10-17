using Syphon.Core;
using Syphon.Desktop;
using Syphon.Engine;

namespace Syphon.Desktop.SmokeTests;

internal static class SizeDisplaySmokeTests
{
    public static int Run() {
        var task=new StoredDownload { Url="https://example.test/video.ts",Destination=@"C:\Downloads\video.ts",PartPath=@"C:\Downloads\.video.part",State=DownloadState.Downloading,Received=131072,EstimatedTotal=1048576,BytesPerSecond=32768 };
        static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
        Check(DownloadDisplay.MediaDuration(task with { MediaDurationSeconds=7132.917 })=="1h 58m 53s", "Video duration formatting is incorrect.");
        Check(DownloadDisplay.MediaDuration(task with { MediaDurationSeconds=double.NaN }) is null, "Invalid duration is displayed.");
        Check(DownloadDisplay.Progress(task).Contains("estimated")&&DownloadDisplay.ExpectedSize(task).Contains("Estimated"),"Estimate is not labelled.");
        Check(DownloadDisplay.Remaining(task).StartsWith("Approx. "),"Estimated time is not labelled.");
        var row=new DownloadRow(task);Check(row.UnknownProgress&&row.ProgressPercent==0&&row.FileType=="Video","An estimate produced a percentage or TS classification failed.");
        Check(DownloadDisplay.Estimate(task with {Received=2097152}) is null,"An exceeded estimate remains visible.");
        Check(DownloadDisplay.Progress(task with {EstimatedTotal=null,Received=0}).Contains("Size not available yet"),"Initial unknown state is missing.");
        Check(DownloadDisplay.Progress(task with {EstimatedTotal=null}).Contains("Size unknown"),"Ongoing unknown state is missing.");
        Check(!DownloadDisplay.Progress(task with {Total=2097152}).Contains("estimated"),"Confirmed size still displays an estimate.");
        Console.WriteLine("PASS dynamic size display: unknown, estimated and confirmed states; approximate ETA; no estimated percentage; TS classification.");return 0;
    }
}
