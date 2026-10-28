using Syphon.Engine;
using Syphon.Core;

internal static class CategoryTests
{
    public static async Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "Syphon.Category.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            await using (var owner = await DownloadCoordinator.OpenAsync(Path.Combine(root, "store"))) {
                var folder = Path.Combine(root, "downloads");
                var cases = new[] { ("Film.MP4", "Videos"), ("track.flac", "Music"), ("report.pdf", "Documents"), ("setup.msix", "Programs"), ("archive.zip", "Compressed"), ("archive.TAR.GZ", "Compressed"), ("resources.7Z", "Compressed"), ("photo.JPG", "Images"), ("picture.avif", "Images"), ("drawing.svg", "Images"), ("unknown", "Misc"), ("captions.vtt", "Documents") };
                foreach (var (name, category) in cases) {
                    var receipt = Guid.NewGuid(); var url = new Uri("https://example.com/" + name);
                    var task = owner.AcceptBrowserDownload(receipt, url, folder, filename: name, startImmediately: false, categorize: true).Task!;
                    if (task.Destination != Path.Combine(folder, category, name) || Path.GetDirectoryName(task.PartPath) != Path.GetDirectoryName(task.Destination)) throw new Exception("Incorrect category or partial-file destination.");
                    if (owner.AcceptBrowserDownload(receipt, url, folder, filename: name, startImmediately: false, categorize: true).TaskId != task.Id) throw new Exception("Retry duplicated a categorized download.");
                }
                if (!DownloadCategories.Names.All(n => Directory.Exists(Path.Combine(folder, n)))) throw new Exception("Missing category folder.");
                var media = owner.AcceptBrowserDownload(Guid.NewGuid(), new Uri("https://example.com/movie.mp4"), folder, mediaFormat: "mp3", startImmediately: false, categorize: true).Task!;
                if (Path.GetDirectoryName(media.Destination) != Path.Combine(folder, "Music")) throw new Exception("Conversion categorized by source rather than output.");
                var custom = owner.AcceptBrowserDownload(Guid.NewGuid(), new Uri("https://example.com/custom.mp4"), folder, startImmediately: false).Task!;
                if (Path.GetDirectoryName(custom.Destination) != folder) throw new Exception("Explicit destination changed.");
                var legacy = owner.AcceptBrowserDownload(Guid.NewGuid(), new Uri("https://example.com/legacy.zip"), Path.Combine(folder,"Misc"), startImmediately:false).Task!;
                if(Path.GetDirectoryName(legacy.Destination)!=Path.Combine(folder,"Misc"))throw new Exception("Legacy archive destination changed.");
                var repeated = owner.AcceptBrowserDownload(Guid.NewGuid(), new Uri("https://example.com/Film.MP4"), folder, filename: "Film.MP4", startImmediately: false, categorize: true).Task!;
                if (!repeated.Destination.EndsWith("Film (2).MP4")) throw new Exception("Categorized collision not handled.");
            }
            await using (var reopened = await DownloadCoordinator.OpenAsync(Path.Combine(root, "store"))) {
                if (!reopened.GetTasks().Any(t => t.Destination.Contains(Path.Combine("Videos", "Film.MP4")))) throw new Exception("Categorized destination did not persist.");
                if(!reopened.GetTasks().Any(t=>t.Destination.Contains(Path.Combine("Misc","legacy.zip"))))throw new Exception("An existing archive was moved to a new category.");
            }
            Console.WriteLine("PASS category routing, all folders, output format, collisions, request retry, custom paths and reopen.");
            return 0;
        } finally {
            var full = Path.GetFullPath(root);
            if (full.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Syphon.Category.Tests")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(full, true);
        }
    }
}

