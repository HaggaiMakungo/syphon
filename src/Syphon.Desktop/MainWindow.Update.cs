using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Syphon.Core;
using Syphon.Engine;

namespace Syphon.Desktop;
public partial class MainWindow
{
    private bool updateInProgress;
    private bool updateExit;
    private bool updateFinalizing;
    private async Task BeginUpdateAsync(ReleaseNotice release, CancellationToken token)
    {
        if (updateInProgress) throw new InvalidOperationException("An update is already being prepared.");
        if (coordinator is null || closing || release.Installer is not { } package) throw new InvalidOperationException("This release has no verified installer available.");
        var root = InstalledUpdateRoot();
        updateInProgress = true; string? file = null; var handedOff = false; var disposalStarted = false;
        var ticketPath = Path.Combine(Path.GetDirectoryName(preferencesPath)!, "update-resume.json");
        try {
            var progress = new Progress<(long Received, long? Total)>(value => updateWindow?.ReportProgress(value.Received, value.Total));
            file = await BackgroundUpdater.DownloadAsync(package, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Syphon", "Updates"), progress, token);
            token.ThrowIfCancellationRequested();
            updateWindow?.ShowReady("Update verified. Windows may now ask for administrator approval. Syphon stays open if approval is declined.");
            // Deny writes/deletion between re-verification and starting the verified executable.
            using var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = await SHA256.HashDataAsync(locked, token);
            if (!Convert.ToHexString(hash).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The downloaded update changed before installation.");
            await Task.Run(() => InstallerSignature.VerifyPackage(file, release.Tag.TrimStart('v')), token);
            using var parent = Process.GetCurrentProcess();
            var ticks = parent.StartTime.ToUniversalTime().Ticks;
            using var ready = new NamedPipeServerStream($"Syphon.Update.{parent.Id}.{ticks}", PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var launch = new ProcessStartInfo(file) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(file)! };
            foreach (var argument in new[] { "--update", root, "--parent-process", parent.Id.ToString(), "--parent-start", ticks.ToString(), "--restart" }) launch.ArgumentList.Add(argument);
            using var installer = Process.Start(launch) ?? throw new IOException("Windows could not start the update installer.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(25));
            while (true) {
                await ready.WaitForConnectionAsync(deadline.Token);
                if (GetNamedPipeClientProcessId(ready.SafePipeHandle, out var client) && client == installer.Id) break;
                ready.Disconnect();
            }
            using var reader = new StreamReader(ready, System.Text.Encoding.UTF8, false, 128, leaveOpen: true);
            if (await reader.ReadLineAsync(deadline.Token) != "READY") throw new IOException("The update installer did not confirm a safe upgrade.");
            updateWindow?.ShowReady("Saving download progress. Syphon will restart after installation…");
            updateFinalizing = true; IsEnabled = false; refresh.Stop();
            new UpdateResumeTicket(release.Tag.TrimStart('v'), DateTimeOffset.UtcNow,
                coordinator.GetTasks().Where(t => t.State is DownloadState.Queued or DownloadState.Starting or DownloadState.Downloading or DownloadState.RetryWaiting or DownloadState.Assembling or DownloadState.Publishing).Select(t => t.Id).ToArray()).Save(ticketPath);
            // This checkpoints and closes active HTTP/media workers before the process exits.
            await coordinator.PauseAllAsync();
            disposalStarted = true;
            await coordinator.DisposeAsync();
            using (var commit = new StreamWriter(ready, new System.Text.UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
                await commit.WriteAsync("COMMIT\n");
            handedOff = true; updateExit = true;
            updateWindow?.AllowShutdown();
            explicitExit = true; Close();
        } catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) {
            throw new OperationCanceledException("Windows update approval was declined. Syphon has not been changed.", ex, token);
        } finally {
            updateInProgress = false;
            if (!handedOff) { try { File.Delete(ticketPath); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            if (!handedOff && updateFinalizing) {
                // A failed disposal still closes the coordinator's resources. Reopen saved
                // state before allowing commands; the missing COMMIT makes setup stop.
                if (disposalStarted) {
                    coordinator = null;
                    try { coordinator = await DownloadCoordinator.OpenAsync(Path.GetDirectoryName(preferencesPath)!, DownloadPreferences.Load(preferencesPath).Transfer); }
                    catch (Exception ex) { initializationError = "Saved downloads could not be reopened. Restart Syphon. " + ex.Message; }
                }
                updateFinalizing = false; IsEnabled = true; refresh.Start();
            }
            if (!handedOff && file is not null) {
                try { File.Delete(file); var directory = Path.GetDirectoryName(file)!; if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }
    private void ResumeAfterUpdate()
    {
        if (!platformEnabled || coordinator is null) return;
        foreach (var id in UpdateResumeTicket.Consume(Path.Combine(Path.GetDirectoryName(preferencesPath)!, "update-resume.json"), ReleaseUpdates.CurrentVersion, DateTimeOffset.UtcNow)) {
            var task = coordinator.GetTasks().FirstOrDefault(t => t.Id == id);
            if (task is { State: DownloadState.Paused, RestartRequired: false, RequiresBrowserContext: false }) {
                try { coordinator.Resume(id); } catch (Exception ex) { RecordPlatformError("A download could not resume after updating: " + ex.Message); }
            }
        }
    }
    private static string InstalledUpdateRoot()
    {
        var executable = Path.GetFullPath(Environment.ProcessPath ?? throw new IOException("Syphon's installation path is unavailable."));
        var app = Path.GetDirectoryName(executable)!; var root = Path.GetDirectoryName(app)!;
        if (!Path.GetFileName(executable).Equals("Syphon.exe", StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(app).Equals("app", StringComparison.OrdinalIgnoreCase)) throw new IOException("In-app updates are available in the installed Syphon app. Use the installer for this development copy.");
        for (var folder = new DirectoryInfo(root); folder is not null; folder = folder.Parent)
            if ((folder.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked installation folders cannot be updated automatically.");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".syphon-install.json")));
        var receipt = document.RootElement;
        if (receipt.GetProperty("Product").GetString() != "Syphon.Desktop" ||
            !string.Equals(Path.GetFullPath(receipt.GetProperty("Root").GetString()!), root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Syphon's installation record does not match this application.");
        return root;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle handle, out uint process);
}
