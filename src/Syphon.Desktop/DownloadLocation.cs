using System.Diagnostics;
using System.IO;
namespace Syphon.Desktop;
internal static class DownloadLocation
{
    internal static ProcessStartInfo OpenFolder(string destination)
    {
        var fullPath=Path.GetFullPath(destination);
        var folder=Path.GetDirectoryName(fullPath);
        if (!Directory.Exists(folder)) throw new IOException("The download folder is no longer available at its saved location.");
        // Explorer's select verb preserves spaces, commas and non-ASCII filenames.
        return File.Exists(fullPath)
            ? new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe")) { UseShellExecute=true, Arguments="/select,\""+fullPath+"\"" }
            : new ProcessStartInfo(folder!) { UseShellExecute=true, Verb="open" };
    }
}
