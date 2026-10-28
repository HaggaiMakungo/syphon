using System.Windows;
using System.Windows.Controls;

namespace Syphon.Desktop;

internal sealed class UpdateSettingsWindow : Window
{
    internal UpdateSettingsWindow(string path, Action saved, Action checkUpdates, Action<bool> diagnostics)
    {
        Title = "Startup, completion and updates"; Width = 570; Height = 690; MinWidth = 390; MinHeight = 360;
        var preferences = DownloadPreferences.Load(path);
        var body = new StackPanel { Margin = new Thickness(24) };
        DialogAppearance.Apply(this, new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, true);
        void Heading(string text) => body.Children.Add(new TextBlock { Text = text, FontSize = 18, Margin = new Thickness(0, 18, 0, 10) });
        CheckBox Toggle(string label, bool value) { var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 5, 0, 5) }; body.Children.Add(box); return box; }
        Button Button(string label, Action click) { var button = new Button { Content = label, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(12, 8, 12, 8) }; button.Click += (_, _) => click(); body.Children.Add(button); return button; }
        Heading("Windows and completion");
        var startup = Toggle("Start with Windows, quietly in the system tray", preferences.StartWithWindows);
        var notifications = Toggle("Notify when downloads finish", preferences.CompletionNotifications);
        var awake = Toggle("Keep Windows awake during active downloads", preferences.PreventSleep);
        Heading("After the queue finishes");
        var action = new ComboBox { ItemsSource = new[] { "None", "Sleep", "Hibernate", "Shutdown", "Command" }, SelectedItem = preferences.CompletionAction, Margin = new Thickness(0, 6, 0, 6) };
        body.Children.Add(action);
        body.Children.Add(new TextBlock { Text = "Power actions and commands are off by default. A countdown lets you cancel. Work paused, failed or cancelled during this queue run prevents the action.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "Countdown seconds (10–300)", Margin = new Thickness(0, 10, 0, 4) });
        var seconds = new TextBox { Text = preferences.CompletionCountdownSeconds.ToString() }; body.Children.Add(seconds);
        body.Children.Add(new TextBlock { Text = "Exact command (runs with your Windows account privileges)", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 4) });
        var command = new TextBox { Text = preferences.CompletionCommand, TextWrapping = TextWrapping.Wrap }; body.Children.Add(command);
        Heading("Transfer protection");
        var transfer = preferences.Transfer ?? new Syphon.Engine.TransferOptions();
        var space = Toggle("Check available disk space", transfer.DiskSpaceProtectionEnabled);
        var adaptive = Toggle("Adapt connections to transfer performance (maximum four)", transfer.AdaptiveConnectionsEnabled);
        var metadata = Toggle("Embed available MP3 metadata", transfer.MediaMetadataEnabled);
        var origin = Toggle("Mark completed files as downloaded from the internet", transfer.MarkOfTheWebEnabled);
        Heading("Updates and support");
        var updates = Toggle("Check for updates at startup and every 12 hours", preferences.CheckForUpdates);
        body.Children.Add(new TextBlock { Text = "Checks GitHub releases. Installation stays under your control.", TextWrapping = TextWrapping.Wrap });
        Button("Check for updates now", checkUpdates);
        Button("Copy diagnostics (private data redacted)", () => diagnostics(true));
        Button("Export diagnostics…", () => diagnostics(false));
        Button("Reset remembered duplicate choice", () => { (DownloadPreferences.Load(path) with { DuplicateChoice = "ask" }).Save(path); DialogAppearance.Notice(this, "Duplicates", "Syphon will ask about duplicate downloads again."); });
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) }; body.Children.Add(error);
        Button("Save", () => {
            try {
                if (!int.TryParse(seconds.Text, out var delay) || delay is < 10 or > 300) throw new ArgumentException("Choose a countdown between 10 and 300 seconds.");
                var selected = (string)action.SelectedItem;
                if (selected == "Command" && string.IsNullOrWhiteSpace(command.Text)) throw new ArgumentException("Enter the exact command or choose None.");
                var latest = DownloadPreferences.Load(path);
                var options = (latest.Transfer ?? new()) with { DiskSpaceProtectionEnabled = space.IsChecked == true, AdaptiveConnectionsEnabled = adaptive.IsChecked == true, MediaMetadataEnabled = metadata.IsChecked == true, MarkOfTheWebEnabled = origin.IsChecked == true };
                var updated = latest with { StartWithWindows = startup.IsChecked == true, CompletionNotifications = notifications.IsChecked == true, PreventSleep = awake.IsChecked == true,
                    CompletionAction = selected, CompletionCountdownSeconds = delay, CompletionCommand = command.Text, CheckForUpdates = updates.IsChecked == true, Transfer = options };
                updated.Save(path); saved(); Close();
            } catch (Exception ex) { error.Text = ex.Message; }
        });
    }
}
