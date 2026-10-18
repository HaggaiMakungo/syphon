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
