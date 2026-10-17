using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
using Microsoft.Win32;
namespace Syphon.Setup;
internal sealed class BrowserSetupWindow : Window
{
 internal BrowserSetupWindow(string root) {
  Title="Connect your browser";Width=610;Height=470;WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
  var border=new Border{CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),BorderBrush=Brush("#6D4A99"),Background=Brush("#0D0914"),Padding=new Thickness(28)};Content=border;
  var panel=new StackPanel();border.Child=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  panel.Children.Add(Text("Connect your browser",24,true));panel.Children.Add(Text("Approve the extension once. Syphon handles the desktop connection.",13));
  var distribution=BrowserDistribution.Load(root);
  var buttons=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,18,0,10)};panel.Children.Add(buttons);
  var status=Text("",13);
  foreach(var edge in new[]{false,true}) {
   var url=distribution.StoreUrl(edge);var button=new Button{Content=edge?"Add to Edge":"Add to Chrome",IsEnabled=url is not null,Margin=new Thickness(0,0,12,0)};
   button.ToolTip=url is null?"Store publication is pending.":"Open the official extension listing. Your browser asks you to approve installation.";
   button.Click+=(_,_)=>{try{OpenBrowser(edge,url!);status.Text="Approve the extension in your browser. It will check the desktop connection after installation.";}catch(Exception ex){status.Text=ex.Message;}};buttons.Children.Add(button);
  }
  panel.Children.Add(Text(distribution.ChromeExtensionId is null&&distribution.EdgeExtensionId is null?"Store listings are not published yet. For this beta, use the extension folder below.":"The store manages future extension updates. Download capture and video detection remain optional.",13));
  var folder=new Button{Content="Open extension folder",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,16,0,8)};
  folder.Click+=(_,_)=>{try{Process.Start(new ProcessStartInfo(Path.Combine(root,"extensions")){UseShellExecute=true});}catch(Exception ex){status.Text=ex.Message;}};panel.Children.Add(folder);
  panel.Children.Add(Text("Beta setup: open chrome://extensions or edge://extensions, enable Developer mode, choose Load unpacked and select this folder. Then open Syphon's extension popup and choose Check connection.",12));
  panel.Children.Add(status);
  var close=new Button{Content="Done",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0),IsCancel=true};close.Click+=(_,_)=>Close();panel.Children.Add(close);
 }
 private static void OpenBrowser(bool edge,string url) {
  var name=edge?"msedge.exe":"chrome.exe";
  foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine}) {
   using var baseKey=RegistryKey.OpenBaseKey(hive,RegistryView.Registry64);
   using var key=baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\"+name);
   if(key?.GetValue("") is string value) {
    var path=value.Trim('"');
    if(File.Exists(path)&&Path.GetFileName(path).Equals(name,StringComparison.OrdinalIgnoreCase)) {var start=new ProcessStartInfo(path){UseShellExecute=true};start.ArgumentList.Add(url);Process.Start(start);return;}
   }
  }
  Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
 }
 private static SolidColorBrush Brush(string value)=>(SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
 private static TextBlock Text(string value,int size,bool heading=false)=>new(){Text=value,FontSize=size,FontWeight=heading?FontWeights.SemiBold:FontWeights.Normal,Foreground=Brush(heading?"#ECE9F2":"#B9AACD"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};
}
