using System.Windows;
using Syphon.BrowserBridge;

namespace Syphon.Desktop;

public partial class App : Application
{
    private Mutex? ownership;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (MainWindow is null)
        {
            ownership = new Mutex(false, "Local\\" + BrowserProtocol.PipeName + ".Desktop");
            bool owned;
            try { owned = ownership.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
            if (!owned) { ownership.Dispose(); ownership = null; Shutdown(0); return; }
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (ownership is not null) { ownership.ReleaseMutex(); ownership.Dispose(); }
        base.OnExit(e);
    }
}
