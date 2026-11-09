using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace QuickScaler
{
    /// <summary>Simple text-input dialog used for renaming presets and similar prompts.</summary>
    public sealed class PromptDialog : Window
    {
        private readonly System.Windows.Controls.TextBox input;

        public string ResultText => input.Text.Trim();

        public PromptDialog(string title, string label, string initialText)
        {
            Title = title;
            Width = 440;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            FontSize = 13;

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            input = new System.Windows.Controls.TextBox { Text = initialText, MinHeight = 32, FontSize = 14, MaxLength = 120 };
            panel.Children.Add(input);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var ok = new Button { Content = "OK", MinWidth = 90, MinHeight = 34, Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)), Foreground = Brushes.White, Margin = new Thickness(0, 0, 8, 0) };
            ok.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(ResultText)) DialogResult = true; };
            var cancel = new Button { Content = "Cancel", MinWidth = 90, MinHeight = 34, IsCancel = true };
            cancel.Click += (_, _) => DialogResult = false;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            Content = new Border { Child = panel, Padding = new Thickness(4) };
            Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        }
    }

    /// <summary>First-run wizard: offers Desk/Couch presets, hotkeys and startup registration.</summary>
    public sealed class FirstRunWindow : Window
    {
        public bool CreateDeskPreset { get; private set; } = true;
        public bool CreateCouchPreset { get; private set; } = true;
        public bool EnableHotkeys { get; private set; } = true;
        public bool EnableStartup { get; private set; }

        public FirstRunWindow(int monitorCount, string currentSummary)
        {
            Title = "Welcome to Quick Scaler";
            Width = 520;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            FontSize = 13;

            var deskCheck = new CheckBox { Content = "Create a \"Desk\" preset (125% — all monitors)", IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
            var couchCheck = new CheckBox { Content = "Create a \"Couch\" preset (200% — all monitors)", IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
            var hotkeyCheck = new CheckBox { Content = "Enable hotkeys: Ctrl+Alt+D (Desk), Ctrl+Alt+C (Couch)", IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
            var startupCheck = new CheckBox { Content = "Run automatically with Windows (minimized to tray)", IsChecked = false, Margin = new Thickness(0, 4, 0, 4) };

            var panel = new StackPanel { Margin = new Thickness(18) };
            panel.Children.Add(new TextBlock
            {
                Text = $"Detected {monitorCount} display(s). Current scaling: {currentSummary}.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Switch between desk and couch distances in one click (or with the hotkeys above). You can change everything later in the dashboard.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
            panel.Children.Add(deskCheck);
            panel.Children.Add(couchCheck);
            panel.Children.Add(hotkeyCheck);
            panel.Children.Add(startupCheck);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var finish = new Button { Content = "Finish", MinWidth = 100, MinHeight = 36, Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)), Foreground = Brushes.White, Margin = new Thickness(0, 0, 8, 0) };
            finish.Click += (_, _) =>
            {
                CreateDeskPreset = deskCheck.IsChecked == true;
                CreateCouchPreset = couchCheck.IsChecked == true;
                EnableHotkeys = hotkeyCheck.IsChecked == true;
                EnableStartup = startupCheck.IsChecked == true;
                DialogResult = true;
            };
            var skip = new Button { Content = "Skip", MinWidth = 100, MinHeight = 36, IsCancel = true };
            skip.Click += (_, _) => DialogResult = false;
            buttons.Children.Add(finish);
            buttons.Children.Add(skip);
            panel.Children.Add(buttons);

            Content = new Border { Child = panel, Padding = new Thickness(4) };
        }
    }
}
