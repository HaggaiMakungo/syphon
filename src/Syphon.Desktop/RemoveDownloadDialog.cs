using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.Json;

namespace Syphon.Desktop;

public sealed class RemoveDownloadDialog : Window
{
    private readonly CheckBox delete;
    public bool DeletePartial => delete.IsChecked == true;
    public RemoveDownloadDialog(string filename, bool unfinished)
    {
        Title = "Remove download"; Width = 520; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Surface"); Foreground = (Brush)FindResource("Text");
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = $"Remove “{filename}” from the list?", FontSize = 17, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Completed files will stay where you saved them.", Margin = new Thickness(0, 10, 0, 16) });
        if (unfinished) panel.Children.Add(new TextBlock { Text = "Removing the task also removes its resume history. Kept unfinished data will remain on disk.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        delete = new CheckBox { Name = "DeletePartialChoice", Content = "Also delete this download’s unfinished data", Visibility = unfinished ? Visibility.Visible : Visibility.Collapsed,
            Foreground = Foreground, Margin = new Thickness(0, 0, 0, 18) };
        panel.Children.Add(delete);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Keep download", IsCancel = true };
        var remove = new Button { Name = "ConfirmRemoval", Content = "Remove", IsDefault = true };
        remove.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(remove); panel.Children.Add(buttons);
        Content = CustomWindowFrame.WrapNativeDialog(this, panel);
        NameScope.SetNameScope(this, new NameScope());
        RegisterName("ConfirmRemoval", remove);
        RegisterName("DeletePartialChoice", delete);
        Width=514*LocalHtmlSurface.InterfaceScale;Height=Math.Min(640*LocalHtmlSurface.InterfaceScale,SystemParameters.WorkArea.Height-40);SizeToContent=SizeToContent.Manual;
        _=new LocalHtmlSurface(this,(FrameworkElement)Content,()=>new {kind="confirmation",confirmation="remove",filename,unfinished},message=>{
            var action=message.GetProperty("action").GetString();
            if(action=="confirm"){delete.IsChecked=message.TryGetProperty("deletePartial",out var choice)&&choice.GetBoolean();DialogResult=true;}
            else if(action=="close")Close();
            else if(action=="layout"){var height=message.GetProperty("height").GetDouble();if(double.IsFinite(height)&&height is >=200 and <=1600)Height=Math.Min(height*LocalHtmlSurface.InterfaceScale,SystemParameters.WorkArea.Height-40);}
            return Task.CompletedTask;
        });
    }
}
