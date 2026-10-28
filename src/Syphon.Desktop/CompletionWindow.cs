using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Syphon.Core;
using Syphon.Engine;
namespace Syphon.Desktop;

/// <summary>Completion actions operate on the published local file, including a native FileDrop drag.</summary>
public sealed class CompletionWindow : Window
{
    private readonly string path;
    private readonly TextBlock error;
    public CompletionWindow(StoredDownload task)
    {
        if(task.State!=DownloadState.Completed)throw new ArgumentException("Only completed downloads have a completion dialog.",nameof(task));
        path=Path.GetFullPath(task.Destination);
        Title="Download complete"; Width=600;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Background=(Brush)Application.Current.FindResource("Surface");Foreground=(Brush)Application.Current.FindResource("Text");
        var body=new StackPanel {Margin=new Thickness(24)};
        body.Children.Add(new TextBlock {Text="Download complete",FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
        body.Children.Add(new TextBlock {Text=task.Filename,TextWrapping=TextWrapping.Wrap,FontSize=16});
        body.Children.Add(new TextBox {Text=path,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12),BorderThickness=new Thickness(0)});
        error=new TextBlock {Foreground=Brushes.IndianRed,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};body.Children.Add(error);
        var actions=new WrapPanel();
        void Add(string text,Action action){var button=new Button {Content=text,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,8)};button.Click+=(_,_)=>{try{action();}catch(Exception ex){error.Text=ex.Message;}};actions.Children.Add(button);}
        Add("Open",()=>{RequireFile();Process.Start(new ProcessStartInfo(path){UseShellExecute=true});});
        Add("Open with",()=>{RequireFile();var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"rundll32.exe")){UseShellExecute=false};info.ArgumentList.Add("shell32.dll,OpenAs_RunDLL");info.ArgumentList.Add(path);Process.Start(info);});
        Add("Open folder",()=>{RequireFile();Process.Start(new ProcessStartInfo(Path.GetDirectoryName(path)!){UseShellExecute=true});});
        Add("Copy file path",()=>Clipboard.SetText(path));Add("Close",Close);body.Children.Add(actions);
        var drag=new Border {HorizontalAlignment=HorizontalAlignment.Right,Padding=new Thickness(14),Margin=new Thickness(0,12,0,0),Background=(Brush)Application.Current.FindResource("Inset"),CornerRadius=new CornerRadius(10),Cursor=Cursors.Hand,ToolTip="Drag the completed file into a folder or another app",Child=new TextBlock {Text="⠿  Drag file",FontSize=14}};
        System.Windows.Automation.AutomationProperties.SetName(drag,"Drag completed file");
        Point? start=null;drag.MouseLeftButtonDown+=(_,e)=>start=e.GetPosition(drag);
        drag.MouseMove+=(_,e)=>{if(e.LeftButton!=MouseButtonState.Pressed){start=null;return;}if(start is not {} origin)return;var now=e.GetPosition(drag);if(Math.Abs(now.X-origin.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(now.Y-origin.Y)<SystemParameters.MinimumVerticalDragDistance)return;start=null;try{var data=CreateFileDrop(path);DragDrop.DoDragDrop(drag,data,DragDropEffects.Copy|DragDropEffects.Move|DragDropEffects.Link);}catch(Exception ex){error.Text=ex.Message;}};
        body.Children.Add(drag);DialogAppearance.Apply(this,body);
        if(!File.Exists(path))error.Text="This file was moved or deleted. Locate it or download it again from the download list.";
    }
    private void RequireFile(){if(!File.Exists(path))throw new FileNotFoundException("This file was moved or deleted. Locate it or download it again from the download list.",path);}
    public static DataObject CreateFileDrop(string path)
    {
        var local=Path.GetFullPath(path);if(!File.Exists(local))throw new FileNotFoundException("The completed file no longer exists.",local);
        var data=new DataObject();data.SetData(DataFormats.FileDrop,new[]{local});return data;
    }
}

