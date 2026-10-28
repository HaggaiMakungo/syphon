using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
namespace Syphon.Desktop;

/// <summary>The same Obsidian palette and native controls used by Syphon's existing dialogs.</summary>
public static class DialogAppearance
{
    public static void Apply(Window window, UIElement body, bool resizable=false)
    {
        window.Background=(Brush)Application.Current.FindResource("Surface");
        window.Foreground=(Brush)Application.Current.FindResource("Text");
        window.FontFamily=new FontFamily(new Uri("pack://application:,,,/Syphon;component/Assets/Fonts/"), "./#Inter");window.FontSize=13;
        window.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        window.Content=CustomWindowFrame.WrapNativeDialog(window,body);
        if(resizable)CustomWindowFrame.Install(window,true);
        window.Resources.Add(typeof(RadioButton),XamlReader.Parse("""
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="RadioButton">
<Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Cursor" Value="Hand"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/>
<Setter Property="Template"><Setter.Value><ControlTemplate TargetType="RadioButton"><Border x:Name="Face" Padding="14" CornerRadius="12" Background="{DynamicResource Card}" BorderBrush="{DynamicResource Line}" BorderThickness="1"><Grid><Grid.ColumnDefinitions><ColumnDefinition Width="22"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions><Border Width="16" Height="16" CornerRadius="8" BorderThickness="1" BorderBrush="{DynamicResource Muted}" VerticalAlignment="Center"><Ellipse x:Name="Dot" Width="8" Height="8" Fill="{DynamicResource Accent}" Visibility="Hidden"/></Border><ContentPresenter Grid.Column="1" Margin="10,0,0,0" VerticalAlignment="Center"/></Grid></Border><ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="Dot" Property="Visibility" Value="Visible"/><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Mid}"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Bright}"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
</Style>
"""));
    }
    public static void Notice(Window owner,string title,string message)
    {
        var window=new Window {Title=title,Owner=owner,Width=460,SizeToContent=SizeToContent.Height};
        var body=new StackPanel {Margin=new Thickness(26)};
        body.Children.Add(new TextBlock {Text=message,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,22)});
        var close=new Button {Content="OK",HorizontalAlignment=HorizontalAlignment.Right,IsDefault=true};close.Click+=(_,_)=>window.Close();body.Children.Add(close);
        Apply(window,body);window.ShowDialog();
    }
    public static Window Countdown(out TextBlock text)
    {
        var window=new Window {Title="Queue completion action",Width=470,SizeToContent=SizeToContent.Height,Topmost=true};
        var panel=new StackPanel {Margin=new Thickness(26)};
        panel.Children.Add(new TextBlock {Text="Your downloads have finished",FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,16)});
        text=new TextBlock {TextWrapping=TextWrapping.Wrap,Foreground=(Brush)Application.Current.FindResource("Sub"),Margin=new Thickness(0,0,0,22)};panel.Children.Add(text);
        var cancel=new Button {Name="CancelQueueAction",Content="Cancel action",HorizontalAlignment=HorizontalAlignment.Right};cancel.Click+=(_,_)=>window.Close();panel.Children.Add(cancel);
        Apply(window,panel);return window;
    }
    public static Window Update(string tag,string notes,Action download,Action later,Action skip)
    {
        var window=new Window {Title="Syphon update",Width=590,Height=510,MinWidth=400,MinHeight=360};
        var body=new StackPanel {Margin=new Thickness(26)};
        body.Children.Add(new TextBlock {Text="Syphon "+tag+" is available",FontSize=22,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18)});
        body.Children.Add(new TextBox {Text=notes,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=240,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        void Add(string label,Action action,bool primary=false){var b=new Button {Content=label,Margin=new Thickness(0,12,0,0)};if(primary){b.Style=(Style)Application.Current.FindResource("PrimaryButton");b.Background=new LinearGradientBrush(Color.FromRgb(0x74,0x4b,0xe0),Color.FromRgb(0x4b,0x24,0xac),90);}b.Click+=(_,_)=>{action();window.Close();};body.Children.Add(b);}
        Add("Download update · opens release page",download,true);Add("Remind me later",later);Add("Skip this version",skip);
        Apply(window,new ScrollViewer {Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},true);return window;
    }
}
