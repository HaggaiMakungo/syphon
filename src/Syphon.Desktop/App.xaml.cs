using System.Windows;
using Syphon.BrowserBridge;

namespace Syphon.Desktop;

public partial class App : Application
{
    private Mutex? ownership;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (MainWindow is null)
        {
            ownership = new Mutex(false, "Local\\" + BrowserProtocol.PipeName + ".Desktop");
            bool owned;
            try { owned = ownership.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
            if (!owned) {
                ownership.Dispose(); ownership = null;
                try { await BrowserPipeClient.SendAsync(BrowserProtocol.PipeName, new BrowserRequest(1, "activate", Guid.NewGuid()), TimeSpan.FromSeconds(3), expectedServerExecutable: Environment.ProcessPath); } catch (Exception) { /* Existing instance may still be starting or shutting down. */ }
                Shutdown(0); return;
            }
            MainWindow = new MainWindow();
            if (e.Args.Contains("--startup", StringComparer.Ordinal)) {
                var manager = (MainWindow)MainWindow;
                // Loaded must run to start the coordinator/WebView, but no visible
                // frame, activation or taskbar button should appear at sign-in.
                manager.ShowActivated = false; manager.ShowInTaskbar = false; manager.Opacity = 0;
                EventHandler? rendered = null;
                rendered = (_, _) => {
                    manager.ContentRendered -= rendered;
                    if (manager.CanHideAtStartup) manager.Hide();
                    manager.Opacity = 1; manager.ShowInTaskbar = true; manager.ShowActivated = true;
                    if (!manager.CanHideAtStartup) manager.RestoreFromTray();
                };
                manager.ContentRendered += rendered;
            }
            ((MainWindow)MainWindow).EnableTray();
            MainWindow.Show();
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (ownership is not null) { ownership.ReleaseMutex(); ownership.Dispose(); }
        base.OnExit(e);
    }
}
