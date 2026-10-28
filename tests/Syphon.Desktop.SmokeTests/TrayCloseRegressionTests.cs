using System.Reflection;
using System.Windows;
using Syphon.Desktop;
namespace Syphon.Desktop.SmokeTests;

internal static class TrayCloseRegressionTests
{
    internal static int Run()
    {
        var app = new App(); app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown; app.MainWindow = new Window();
        var result = 1;
        app.Startup += (_, _) => {
            try {
                var type = typeof(MainWindow).Assembly.GetType("Syphon.Desktop.TrayPopupWindow")!;
                var deactivate = typeof(Window).GetMethod("OnDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!;
                for (var i = 0; i < 12; i++) {
                    var window = (Window)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
                        new object[] { (Action<string>)(_ => { }), (Func<object>)(() => new { view = "tray", active = 0, speed = "0 B/s" }) }, null)!;
                    window.Left = -10000; window.Top = -10000; window.ShowActivated = false;
                    var closed = 0;
                    window.Closed += (_, _) => closed++;
                    // Windows can deactivate the popup during its native close sequence.
                    window.Closing += (_, _) => deactivate.Invoke(window, new object[] { EventArgs.Empty });
                    window.Show();
                    if (i % 2 == 0) window.Close();
                    else deactivate.Invoke(window, new object[] { EventArgs.Empty });
                    if (closed != 1) throw new Exception("Tray popup did not close exactly once.");
                }
                Console.WriteLine("PASS tray close: explicit close and focus-loss close tolerate deactivation during closing across 12 popups.");
                result = 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { app.Shutdown(result); }
        };
        app.Run(); return result;
    }
}
