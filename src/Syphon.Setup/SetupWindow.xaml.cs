using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Win32;
namespace Syphon.Setup;
public partial class SetupWindow : Window
{
 private int step;private bool busy;private readonly bool uninstall;private string installedRoot="";private readonly CancellationTokenSource cancellation=new();
 public SetupWindow(bool uninstall=false) {
  InitializeComponent();this.uninstall=uninstall;
  VersionLabel.Text=(typeof(SetupWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "Beta")+" · Windows x64";
  Width=Math.Min(1060,SystemParameters.WorkArea.Width-32);Height=Math.Min(700,SystemParameters.WorkArea.Height-32);
  Location.Text=InstallEngine.DefaultRoot;
  LaunchOption.IsChecked=false;LaunchOption.Visibility=Visibility.Collapsed;FinishNote.Text="Launch Syphon from the Start menu or desktop shortcut. Your downloads remain in your own folders.";
  if(uninstall){
   Title="Uninstall Syphon";WindowCaption.Text=Title;Steps.Children[1].Visibility=Visibility.Collapsed;Steps.Children[3].Visibility=Visibility.Collapsed;
   ((TextBlock)((Border)Steps.Children[0]).Child).Text="○    Confirm removal";
   ((TextBlock)((Border)Steps.Children[2]).Child).Text="○    Removing";
   ((TextBlock)((Border)Steps.Children[4]).Child).Text="○    Done";
   Location.Text=AppContext.BaseDirectory;Heading.Text="Remove Syphon";Subtitle.Text="Your downloaded files, history and preferences will stay.";WelcomePanel.Visibility=Visibility.Collapsed;Next.Content="Uninstall";}
  Closing+=(_,e)=>{if(busy)e.Cancel=true;};ShowStep(0);
 }
 private void ShowStep(int value) {
  step=value;Error.Visibility=Visibility.Collapsed;UninstallPanel.Visibility=uninstall&&step==0?Visibility.Visible:Visibility.Collapsed;
  for(int i=0;i<5;i++){var border=(Border)Steps.Children[i];border.Background=new SolidColorBrush(i==step?Color.FromArgb(100,54,35,80):Colors.Transparent);border.BorderBrush=new SolidColorBrush(i==step?Color.FromRgb(182,115,255):Colors.Transparent);((TextBlock)border.Child).Foreground=new SolidColorBrush(i<=step?Color.FromRgb(236,233,242):Color.FromRgb(146,132,170));}
  WelcomePanel.Visibility=!uninstall&&step==0?Visibility.Visible:Visibility.Collapsed;LocationPanel.Visibility=!uninstall&&step==1?Visibility.Visible:Visibility.Collapsed;InstallPanel.Visibility=step==2?Visibility.Visible:Visibility.Collapsed;BrowserPanel.Visibility=!uninstall&&step==3?Visibility.Visible:Visibility.Collapsed;FinishPanel.Visibility=step==4?Visibility.Visible:Visibility.Collapsed;
  Back.Visibility=step==1?Visibility.Visible:Visibility.Collapsed;Cancel.Visibility=step>=3?Visibility.Collapsed:Visibility.Visible;
  Next.IsEnabled=step!=3||ReadyBrowser.IsChecked==true||SkipBrowser.IsChecked==true;
  if(uninstall){Heading.Text=step==2?"Removing Syphon":step==4?"Syphon has been removed":"Uninstall Syphon";Subtitle.Text="Your downloads, history and preferences will be preserved.";Next.Content=step==4?"Close":"Uninstall";}
  if(!uninstall){Heading.Text=step switch{0=>"Welcome to Syphon",1=>"Choose your workspace",2=>"Installing Syphon",3=>"Connect your browser",_=>"You're ready to download"};Subtitle.Text=step switch{0=>"Set up your download workspace.",1=>"Everything you need, in one place.",2=>"Preparing your download workspace.",3=>"One last step before you finish.",_=>"Syphon is installed on this computer."};Next.Content=step==1?"Install":step==4?"Finish":"Continue";}
 }
 private void DragHeader(object sender,MouseButtonEventArgs e){if(e.ChangedButton==MouseButton.Left)DragMove();}
 private void BrowseClick(object sender,RoutedEventArgs e){var picker=new OpenFolderDialog{Title="Choose the Syphon installation folder"};if(picker.ShowDialog(this)==true)Location.Text=picker.FolderName;}
 private void BackClick(object sender,RoutedEventArgs e)=>ShowStep(0);
 private void CancelClick(object sender,RoutedEventArgs e){if(busy){cancellation.Cancel();Cancel.IsEnabled=false;Status.Text="Cancelling safely…";}else Close();}
 private async void NextClick(object sender,RoutedEventArgs e) {
  if(busy)return;
  if(!uninstall&&step==3){
   if(ReadyBrowser.IsChecked!=true&&SkipBrowser.IsChecked!=true)return;
   try{if(ReadyBrowser.IsChecked==true)InstallEngine.EnableBrowserIntegration(installedRoot);ShowStep(4);}
   catch(Exception ex){Error.Text=ex.Message;Error.Visibility=Visibility.Visible;}
   return;
  }
  if(step==4){if(!uninstall&&LaunchOption.IsChecked==true)try{Process.Start(new ProcessStartInfo(Path.Combine(installedRoot,"app","Syphon.exe")){UseShellExecute=true});}catch(Exception ex){Error.Text=ex.Message;Error.Visibility=Visibility.Visible;return;}Close();return;}
  if(!uninstall&&step==0){ShowStep(1);return;}
  try {
   var root=uninstall?Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)):InstallEngine.ValidateRoot(Location.Text.Trim());
   if(!uninstall&& !Environment.Is64BitOperatingSystem)throw new IOException("This installer requires 64-bit Windows.");
   if(uninstall){StartUninstallerCopy(root);return;}
   var browser=false;var desktop=DesktopOption.IsChecked==true;
   ShowStep(2);busy=true;Next.IsEnabled=false;Back.IsEnabled=false;
   Status.Text="Verifying the installer and available space…";
   await Task.Run(()=>{using var payload=InstallEngine.OpenPayload();using var zip=new ZipArchive(payload,ZipArchiveMode.Read);var needed=zip.Entries.Sum(f=>f.Length)+200L*1024*1024;var drive=new DriveInfo(Path.GetPathRoot(root)!);if(drive.AvailableFreeSpace<needed)throw new IOException($"Setup needs at least {needed/1048576:N0} MB free on this drive.");});
   if(!WindowsIntegration.WebViewPresent()) {
    Status.Text="Installing Microsoft WebView2. An internet connection is required…";Cancel.IsEnabled=false;
    await Task.Run(Prerequisites.EnsureWebView);Cancel.IsEnabled=true;
   }
   var progress=new Progress<(double,string)>(value=>{Progress.Value=value.Item1;Percent.Text=$"{value.Item1:0}%";Status.Text=value.Item2;});
   var receipt=await Task.Run(()=>{using var payload=InstallEngine.OpenPayload();return InstallEngine.Install(payload,root,browser,desktop,progress,cancellation.Token);});
   installedRoot=receipt.Root;busy=false;Next.IsEnabled=true;ShowStep(3);FinishPath.Text=installedRoot;ExtensionPath.Text=Path.Combine(installedRoot,"Extensions");

  } catch(Exception ex) {
   busy=false;Next.IsEnabled=true;Back.IsEnabled=true;Cancel.IsEnabled=true;
   ShowStep(uninstall?0:1);Error.Text=ex is OperationCanceledException?"Installation cancelled. Your previous installation was preserved. Close setup and run it again when ready.":ex.Message;Error.Visibility=Visibility.Visible;
   if(cancellation.IsCancellationRequested)Next.IsEnabled=false;
  }
 }
 private void StartUninstallerCopy(string root) {
  _=InstallEngine.ReadReceipt(root);InstallEngine.CheckRunning(root);
  var current=Environment.ProcessPath!;var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Syphon-Uninstall-"+Guid.NewGuid().ToString("N"));InstallEngine.NoLinks(folder);Directory.CreateDirectory(folder);
  var copy=Path.Combine(folder,"Syphon.Uninstall.exe");File.Copy(current,copy);
  Process.Start(CreateUninstallStart(copy,root));Close();
 }
 internal static ProcessStartInfo CreateUninstallStart(string executable,string root) {
  var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(executable)!};
  start.ArgumentList.Add("--remove-from");start.ArgumentList.Add(root);start.ArgumentList.Add("--parent-process");start.ArgumentList.Add(Environment.ProcessId.ToString());return start;
 }
 internal static void WaitForUninstallerExit(int parentProcessId) {
  if(parentProcessId<=0)return;
  if(parentProcessId==Environment.ProcessId)throw new IOException("The removal helper cannot wait for itself.");
  Process parent;try{parent=Process.GetProcessById(parentProcessId);}catch(ArgumentException){return;}
  using(parent)if(!parent.WaitForExit(30000))throw new IOException("The original uninstaller is still closing. Close it, then retry removal.");
 }
 internal void CompleteRemoval() {
  busy=false;Next.IsEnabled=true;Progress.Value=100;Percent.Text="100%";Status.Text="Syphon has been removed.";ShowStep(4);
  Heading.Text="Syphon has been removed";Subtitle.Text="Your downloads, history and preferences were preserved.";LaunchOption.Visibility=Visibility.Collapsed;ExtensionHelp.Visibility=Visibility.Collapsed;
  FinishNote.Text="The application and its shortcuts have been removed. You can close this window. Your browser extension can be removed separately in your browser.";FinishPath.Text="";Next.Content="Close";Next.Focus();
 }
 internal void RemoveFrom(string root,int parentProcessId=0) {
  Next.Content="Uninstall";
  Next.Click-=NextClick;Next.Click+=async (_,_)=>{if(step==4){Close();return;}if(busy)return;try{busy=true;Next.IsEnabled=false;Cancel.IsEnabled=false;ShowStep(2);Status.Text="Removing application files. Keeping your downloads and history…";await Task.Run(()=>{WaitForUninstallerExit(parentProcessId);InstallEngine.Uninstall(root);});CompleteRemoval();}catch(Exception ex){busy=false;Next.IsEnabled=true;Cancel.IsEnabled=true;Error.Text=ex.Message;Error.Visibility=Visibility.Visible;}};
 }
 internal void ContinueConfirmedRemoval(){Loaded+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=>Next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))),System.Windows.Threading.DispatcherPriority.ApplicationIdle);}
 private void BrowserChoiceChanged(object sender,RoutedEventArgs e){if(step==3)Next.IsEnabled=ReadyBrowser.IsChecked==true||SkipBrowser.IsChecked==true;}
 private string ExtensionsFolder=>Path.Combine(installedRoot,"Extensions");
 private void OpenExtensionsClick(object sender,RoutedEventArgs e){try{Process.Start(new ProcessStartInfo(ExtensionsFolder){UseShellExecute=true});}catch(Exception ex){Error.Text=ex.Message;Error.Visibility=Visibility.Visible;}}
 private void CopyExtensionsClick(object sender,RoutedEventArgs e){try{Clipboard.SetText(ExtensionsFolder);}catch(Exception ex){Error.Text=ex.Message;Error.Visibility=Visibility.Visible;}}
 private void ExtensionClick(object sender,RoutedEventArgs e){try{new BrowserSetupWindow(installedRoot){Owner=this}.ShowDialog();}catch(Exception ex){Error.Text=ex.Message;Error.Visibility=Visibility.Visible;}}
 internal void RenderPreview(string destination){Loaded+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=>{Directory.CreateDirectory(destination);ExtensionPath.Text=Path.Combine(InstallEngine.DefaultRoot,"Extensions");foreach(var i in (uninstall?new[]{0,2,4}:new[]{0,1,2,3,4})){ShowStep(i);UpdateLayout();var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);using var output=File.Create(Path.Combine(destination,$"setup-{i}.png"));var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(output);}Application.Current.Shutdown();}),System.Windows.Threading.DispatcherPriority.ApplicationIdle);}
}
