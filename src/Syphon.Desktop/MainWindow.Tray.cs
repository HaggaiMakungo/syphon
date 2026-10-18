using System.Windows;
namespace Syphon.Desktop;
public partial class MainWindow
{
    private SystemTray? tray;
    private bool explicitExit;
    internal void EnableTray()
    {
        if(tray is not null)return;
        tray=new SystemTray(TrayCommand);
        StateChanged+=(_,_)=> { if(!closing && WindowState==WindowState.Minimized && DownloadPreferences.Load(preferencesPath).MinimizeToTray && tray.IsRegistered) Hide(); };
    }
    internal void RestoreFromTray()
    {
        if(closing)return;
        Show(); if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;
        Activate();
    }
    internal void ExitFromTray() { explicitExit=true; Close(); }
    private async void TrayCommand(string action)
    {
        try {
            if(action=="open")RestoreFromTray();
            else if(action=="exit")ExitFromTray();
            else if(IsReady && coordinator is not null) {
                if(action=="pause")await coordinator.PauseAllAsync();
                else if(action=="resume")coordinator.ResumeAll();
                RefreshTasks();
            }
        } catch(Exception ex) { commandError=ex.Message; HtmlSurface?.Publish(); }
    }
}
