using System.Diagnostics;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace Syphon.Desktop;
internal static class DownloadLocation
{
    internal static void Launch(ProcessStartInfo start)
    {
        if (Path.GetFileName(start.FileName).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
            && start.Arguments.StartsWith("/select,\"", StringComparison.Ordinal) && start.Arguments.EndsWith('"'))
        {
            SelectFile(start.Arguments[9..^1]);
            return;
        }
        Process.Start(start);
    }
    internal static void SelectFile(string path)
    {
        // Ask the running Windows shell to select the file, rather than starting
        // another Explorer process whose command line can be silently ignored.
        var result=SHParseDisplayName(path,IntPtr.Zero,out var item,0,out _);
        if(result<0)throw new Win32Exception(result,"Windows could not locate the downloaded file.");
        try {
            result=SHOpenFolderAndSelectItems(item,0,IntPtr.Zero,0);
            if(result<0)throw new Win32Exception(result,"Windows could not open the download folder.");
        } finally { Marshal.FreeCoTaskMem(item); }
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name,IntPtr binding,out IntPtr item,uint attributes,out uint actual);
    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr item,uint count,IntPtr children,uint flags);
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
