using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Syphon.Desktop;

internal static class WindowsIntegration
{
    internal static string StartupCommand(string executable) => "\"" + executable + "\" --startup";
    internal static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("Syphon", StartupCommand(Environment.ProcessPath!));
        else key.DeleteValue("Syphon", false);
    }
    internal static void KeepAwake(bool enabled) => SetThreadExecutionState(enabled ? 0x80000001u : 0x80000000u);
    internal static void RunCompletionAction(string action, string command)
    {
        KeepAwake(false);
        switch (action) {
            case "Sleep": if (!SetSuspendState(false, false, false)) throw new InvalidOperationException("Windows could not enter sleep."); break;
            case "Hibernate": if (!SetSuspendState(true, false, false)) throw new InvalidOperationException("Windows could not hibernate. Check that hibernation is available."); break;
            case "Shutdown": Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0") { UseShellExecute = false, CreateNoWindow = true }); break;
            case "Command":
                if (string.IsNullOrWhiteSpace(command)) throw new InvalidOperationException("Choose a completion command first.");
                Process.Start(new ProcessStartInfo("cmd.exe") { Arguments = "/d /s /c \"" + command + "\"", UseShellExecute = false, CreateNoWindow = true }); break;
        }
    }
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
    [DllImport("powrprof.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate, [MarshalAs(UnmanagedType.U1)] bool force, [MarshalAs(UnmanagedType.U1)] bool disableWake);
}
