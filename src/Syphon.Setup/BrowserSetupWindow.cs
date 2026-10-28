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
  Title="Connect your browser";Width=560;Height=Math.Min(720,SystemParameters.WorkArea.Height-40);WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
  var frame=new Border{CornerRadius=new CornerRadius(32),BorderThickness=new Thickness(1),BorderBrush=Brush("#58425F"),Background=Brush("#121014")};Content=frame;
  var layout=new DockPanel();frame.Child=layout;
  var chrome=new DockPanel{Height=53,LastChildFill=true};
  var close=new Button{Content="×",Width=30,Height=30,Padding=new Thickness(0),Background=Brushes.Transparent,Margin=new Thickness(0,0,14,0)};close.Click+=(_,_)=>Close();DockPanel.SetDock(close,Dock.Right);chrome.Children.Add(close);
  var caption=Text("Syphon    Browser setup",13,true);caption.VerticalAlignment=VerticalAlignment.Center;caption.Margin=new Thickness(22,0,0,0);chrome.Children.Add(caption);chrome.MouseLeftButtonDown+=(_,e)=>{if(e.LeftButton==System.Windows.Input.MouseButtonState.Pressed)DragMove();};var chromeFace=new Border{CornerRadius=new CornerRadius(32,32,0,0),Background=Brush("#201A24"),Child=chrome};DockPanel.SetDock(chromeFace,Dock.Top);layout.Children.Add(chromeFace);
  var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(25,18,25,20)};var done=new Button{Content="Done",MinWidth=110,Background=Brush("#8A36AD"),IsCancel=true};done.Click+=(_,_)=>Close();footer.Children.Add(done);DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);
  var panel=new StackPanel{Margin=new Thickness(27,27,27,8)};layout.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
  panel.Children.Add(Text("Connect your browser",27,true));panel.Children.Add(Text("A quick introduction, then you’re ready.",12));
  var browsers=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,18,0,23)};panel.Children.Add(browsers);var address=Text("chrome://extensions",13,true);var status=Text("",11);var selectedEdge=false;
  var chromeButton=new Button{Content="Chrome",Width=126,Background=Brush("#4B2D59"),Margin=new Thickness(0,0,10,0)};var edgeButton=new Button{Content="Edge",Width=126};
  chromeButton.Click+=(_,_)=>{selectedEdge=false;address.Text="chrome://extensions";chromeButton.Background=Brush("#4B2D59");edgeButton.Background=Brush("#28212F");};
  edgeButton.Click+=(_,_)=>{selectedEdge=true;address.Text="edge://extensions";edgeButton.Background=Brush("#4B2D59");chromeButton.Background=Brush("#28212F");};browsers.Children.Add(chromeButton);browsers.Children.Add(edgeButton);
  StackPanel Step(string number,string heading){var content=new StackPanel();var row=new Grid{Margin=new Thickness(0,0,0,22)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(46)});row.ColumnDefinitions.Add(new ColumnDefinition());var badge=new Border{Width=31,Height=33,CornerRadius=new CornerRadius(11),Background=Brush("#38243F"),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Left,Child=new TextBlock{Text=number,FontSize=13,Foreground=Brush("#D1A1E4"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};row.Children.Add(badge);Grid.SetColumn(content,1);row.Children.Add(content);content.Children.Add(Text(heading,14,true));panel.Children.Add(row);return content;}
  var first=Step("1","Open the extensions page");first.Children.Add(Text("Paste this address into your browser.",12));var addressRow=new DockPanel();var copyAddress=new Button{Content="Copy",Padding=new Thickness(11,7,11,7),Margin=new Thickness(12,0,0,0)};copyAddress.Click+=(_,_)=>{Clipboard.SetText(address.Text);status.Text="Address copied.";};DockPanel.SetDock(copyAddress,Dock.Right);addressRow.Children.Add(copyAddress);addressRow.Children.Add(address);first.Children.Add(new Border{Background=Brush("#211A27"),CornerRadius=new CornerRadius(15),Padding=new Thickness(13),Child=addressRow});
  var second=Step("2","Add Syphon this");second.Children.Add(Text("Enable Developer mode, choose Load unpacked, and select the extension folder.",12));var folder=Path.Combine(root,"extensions");var openFolder=new Button{Content="Open extension folder ↗",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,6,0,10)};openFolder.Click+=(_,_)=>{try{if(!Directory.Exists(folder))throw new IOException("The extension folder was not found. Run setup with browser integration enabled.");Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}catch(Exception ex){status.Text=ex.Message;}};second.Children.Add(openFolder);second.Children.Add(Text(folder,10));
  var third=Step("3","Check the connection");third.Children.Add(Text("Keep Syphon open. Open the extension and choose Check connection. Refresh existing video tabs afterwards.",12));
  var distribution=BrowserDistribution.Load(root);var store=new Button{Content="Open browser store listing",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,15),Visibility=distribution.ChromeExtensionId is null&&distribution.EdgeExtensionId is null?Visibility.Collapsed:Visibility.Visible};store.Click+=(_,_)=>{var url=distribution.StoreUrl(selectedEdge);if(url is null){status.Text="The store listing for this browser is not available yet.";return;}try{OpenBrowser(selectedEdge,url);}catch(Exception ex){status.Text=ex.Message;}};panel.Children.Add(store);panel.Children.Add(Text("For this beta, use the unpacked extension folder above. Browser permissions remain your choice.",11));panel.Children.Add(status);
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
 private static TextBlock Text(string value,int size,bool heading=false)=>new(){Text=value,FontSize=size,FontWeight=heading?FontWeights.SemiBold:FontWeights.Normal,Foreground=Brush(heading?"#ECE9F2":"#B9A8C3"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};
}
