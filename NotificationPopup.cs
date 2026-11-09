using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;

namespace QuickScaler
{
    /// <summary>
    /// A small bottom-right notification that never steals focus, with an optional
    /// action button (e.g. Revert). Used when the main window is hidden in the tray.
    /// </summary>
    public sealed class NotificationPopup : Window
    {
        private readonly DispatcherTimer timer;
        private readonly TextBlock countdownText;

        public event Action? ActionClicked;

        public NotificationPopup(string title, string message, string? actionLabel = null, int autoCloseSeconds = 15)
        {
            Title = title;
            Width = 400;
            Height = Double.NaN;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Margin = new Thickness(8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                BorderThickness = new Thickness(0, 0, 0, 3)
            };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            panel.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 13,
                Foreground = Brushes.WhiteSmoke,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });

            countdownText = new TextBlock
            {
                FontSize = 11,
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 8, 0, 0)
            };
            panel.Children.Add(countdownText);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            if (!string.IsNullOrWhiteSpace(actionLabel))
            {
                var actionButton = new Button
                {
                    Content = actionLabel,
                    MinWidth = 90,
                    MinHeight = 36,
                    FontSize = 13,
                    Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                actionButton.Click += (_, _) =>
                {
                    Close();
                    ActionClicked?.Invoke();
                };
                buttons.Children.Add(actionButton);
            }

            var closeButton = new Button { Content = "Dismiss", MinWidth = 90, MinHeight = 36, FontSize = 13 };
            closeButton.Click += (_, _) => Close();
            buttons.Children.Add(closeButton);
            panel.Children.Add(buttons);

            border.Child = panel;
            Content = border;

            int secondsLeft = Math.Max(3, autoCloseSeconds);
            UpdateCountdown(secondsLeft);
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                secondsLeft--;
                if (secondsLeft <= 0)
                {
                    timer.Stop();
                    Close();
                }
                else
                {
                    UpdateCountdown(secondsLeft);
                }
            };
            timer.Start();

            Loaded += (_, _) =>
            {
                Left = SystemParameters.WorkArea.Right - Width - 16;
                Top = SystemParameters.WorkArea.Bottom - ActualHeight - 16;
            };
        }

        private void UpdateCountdown(int secondsLeft)
        {
            countdownText.Text = $"Closing in {secondsLeft}s";
        }

        protected override void OnClosed(EventArgs e)
        {
            timer.Stop();
            base.OnClosed(e);
        }
    }
}
