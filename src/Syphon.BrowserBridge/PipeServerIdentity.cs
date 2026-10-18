using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Syphon.BrowserBridge;

internal static class PipeServerIdentity
{
    // Pin the process behind this connected pipe, not a claimed PID in its JSON.
    // No request (including URLs or session context) is written until this succeeds.
    internal static SafeProcessHandle Verify(NamedPipeClientStream pipe, string expectedExecutable)
    {
        if (string.IsNullOrWhiteSpace(expectedExecutable) || !Path.IsPathFullyQualified(expectedExecutable))
            throw Rejected();
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid) || pid == 0) throw Rejected();
        var process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        try
        {
            if (process.IsInvalid) throw Rejected();
            var image = new StringBuilder(32768); var length = image.Capacity;
            if (!QueryFullProcessImageName(process, 0, image, ref length) ||
                !string.Equals(Path.GetFullPath(image.ToString()), Path.GetFullPath(expectedExecutable), StringComparison.OrdinalIgnoreCase) ||
                !GetExitCodeProcess(process, out var status) || status != 259 ||
                !GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var connectedPid) || connectedPid != pid)
                throw Rejected();
            return process;
        }
        catch { process.Dispose(); throw; }
    }

    private static SecurityException Rejected() => new("Syphon could not verify the desktop connection. No browser request was sent.");

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
}
