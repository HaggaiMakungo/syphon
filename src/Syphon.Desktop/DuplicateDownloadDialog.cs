using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Syphon.Engine;
namespace Syphon.Desktop;

public sealed class DuplicateDownloadDialog : Window
{
 private readonly RadioButton numbered, replace, existing;
 private readonly CheckBox remember;
 public string Choice => replace.IsChecked == true ? "replace" : existing.IsChecked == true ? "existing" : "numbered";
 public bool Remember => remember.IsChecked == true;
 public DuplicateDownloadDialog(string filename, bool canReplace) {
  Title="Duplicate download";Width=560;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  Background=(Brush)FindResource("Surface");Foreground=(Brush)FindResource("Text");FontFamily=new FontFamily("Segoe UI");
  NameScope.SetNameScope(this,new NameScope());
  var panel=new StackPanel {Margin=new Thickness(28)};
  panel.Children.Add(new TextBlock {Text="This download already exists",FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
  panel.Children.Add(new TextBlock {Text=filename,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18)});
  numbered=ChoiceButton("DuplicateNumbered","Download a fresh copy with a numbered filename",true);
  replace=ChoiceButton("DuplicateReplace","Replace the old file after the fresh download succeeds",false);replace.IsEnabled=canReplace;
  if(!canReplace)replace.ToolTip="Pause or finish the existing download before replacing its file.";
  existing=ChoiceButton("DuplicateExisting","Use the existing file or resume its unfinished download",false);
  panel.Children.Add(numbered);panel.Children.Add(replace);panel.Children.Add(existing);
  remember=new CheckBox {Name="RememberDuplicateChoice",Content="Remember this choice (change it in Settings)",Margin=new Thickness(0,18,0,20)};RegisterName(remember.Name,remember);panel.Children.Add(remember);
  var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
  var cancel=new Button {Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>{DialogResult=false;};
  var confirm=new Button {Content="Continue",IsDefault=true,Margin=new Thickness(10,0,0,0)};confirm.Click+=(_,_)=>{DialogResult=true;};
  buttons.Children.Add(cancel);buttons.Children.Add(confirm);panel.Children.Add(buttons);Content=panel;
 }
 private RadioButton ChoiceButton(string name,string text,bool selected) {
  var button=new RadioButton {Name=name,Content=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap},GroupName="DuplicateChoice",IsChecked=selected,Margin=new Thickness(0,7,0,7)};RegisterName(name,button);return button;
 }
 public static StoredDownload? FindCandidate(IEnumerable<StoredDownload> tasks,string source,string destination,string? mediaFormat) => tasks
  .Where(t=>string.Equals(t.MediaFormat,mediaFormat,StringComparison.OrdinalIgnoreCase) && (SameUrl(t.Url,source) || string.Equals(Path.GetFullPath(t.Destination),Path.GetFullPath(destination),StringComparison.OrdinalIgnoreCase)))
  .OrderByDescending(t=>t.State==Syphon.Core.DownloadState.Completed).ThenByDescending(t=>t.CreatedAt).FirstOrDefault();
 private static bool SameUrl(string left,string right) => Uri.TryCreate(left,UriKind.Absolute,out var a)&&Uri.TryCreate(right,UriKind.Absolute,out var b)&&a==b;
}
