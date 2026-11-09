using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using MessageBoxResult = System.Windows.MessageBoxResult;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using System.Windows.Interop;
using WpfMedia = System.Windows.Media;
using Forms = System.Windows.Forms;
using Draw = System.Drawing;
using Microsoft.Win32;

namespace QuickScaler
{
    /// <summary>
    /// Dashboard for selecting displays, managing scaling presets, and applying display scaling.
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;
        private const int VK_D = 0x44;
        private const int VK_C = 0x43;
        private const int VK_UP = 0x26;
        private const int VK_DOWN = 0x28;
        private const int HotkeyDesk = 1;
        private const int HotkeyCouch = 2;
        private const int HotkeyScaleUp = 3;
        private const int HotkeyScaleDown = 4;
        private const int RevertCountdownSeconds = 15;

        private Forms.NotifyIcon trayIcon = null!;
        private Draw.Icon? trayAppIcon;
        private readonly List<MonitorDisplayInfo> detectedMonitors = new();
        private readonly ObservableCollection<ScalingPreset> presets = new();
        private Forms.ContextMenuStrip trayContextMenu = null!;
        private readonly CliOptions cliOptions;
        private AppSettings settings = new();
        private bool isExiting;
        private bool suppressSettingsEvents;
        private bool isBusy;
        private bool hotkeysRegistered;
        private IntPtr hotkeyWindowHandle;

        private readonly List<(string DeviceName, int ScalePercent)> pendingRevert = new();
        private readonly System.Windows.Threading.DispatcherTimer revertTimer;
        private readonly System.Windows.Threading.DispatcherTimer displayRefreshTimer;
        private readonly System.Windows.Threading.DispatcherTimer foregroundTimer;
        private DateTime lastManualApplyUtc = DateTime.MinValue;
        private DateTime lastAutoApplyUtc = DateTime.MinValue;
        private string? lastForegroundProcess;

        public MainWindow(CliOptions? options = null)
        {
            cliOptions = options ?? new CliOptions();

            settings = AppSettingsStore.Load();

            InitializeComponent();
            InitializeTrayIcon();

            revertTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            revertTimer.Tick += RevertTimer_Tick;

            displayRefreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            displayRefreshTimer.Tick += (_, _) =>
            {
                displayRefreshTimer.Stop();
                OnDisplaySettingsChanged();
            };

            foregroundTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            foregroundTimer.Tick += (_, _) => CheckForegroundProfile();

            MonitorCanvas.SizeChanged += (_, _) => LayoutMonitorCards();
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChangedEvent;

            PopulateScaleCombo();
            LoadConnectedMonitors();
            LoadPresets();
            PresetsListBox.ItemsSource = presets;
            PresetsListBox.DisplayMemberPath = nameof(ScalingPreset.Name);
            RefreshTrayPresetMenu();
            ApplySettingsToControls();
            UpdateStatusText();

            SourceInitialized += (_, _) =>
            {
                hotkeyWindowHandle = new WindowInteropHelper(this).Handle;
                if (HwndSource.FromHwnd(hotkeyWindowHandle) is HwndSource source)
                    source.AddHook(WndProc);
                if (settings.HotkeysEnabled)
                    TryRegisterHotKeys();
            };

            Loaded += (_, _) =>
            {
                HandleCliOptions();
                if (!settings.FirstRunCompleted && !cliOptions.StartMinimized && !cliOptions.HasApplyAction)
                    ShowFirstRunWizard();
            };

            if (settings.AppProfiles.Count > 0)
                foregroundTimer.Start();
        }

        // ---------- Tray ----------

        private void InitializeTrayIcon()
        {
            Draw.Icon? icon = null;
            try
            {
                string? executablePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(executablePath))
                    icon = Draw.Icon.ExtractAssociatedIcon(executablePath);
            }
            catch
            {
                icon = null;
            }

            trayAppIcon = icon ?? System.Drawing.SystemIcons.Application;

            trayIcon = new Forms.NotifyIcon
            {
                Icon = trayAppIcon,
                Visible = true,
                Text = "Quick Scaler"
            };

            trayContextMenu = new Forms.ContextMenuStrip();
            trayIcon.ContextMenuStrip = trayContextMenu;
            trayIcon.DoubleClick += (s, e) => ShowDashboard();

            Closed += (_, _) =>
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChangedEvent;
                UnregisterHotKeys();
                revertTimer.Stop();
                displayRefreshTimer.Stop();
                foregroundTimer.Stop();
                try { trayIcon.Visible = false; } catch { }
                trayIcon.Dispose();
            };
        }

        private void ShowDashboard()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void RefreshTrayPresetMenu()
        {
            trayContextMenu.Items.Clear();
            List<string> labels = TrayMenuBuilder.BuildLabels(presets.Select(p => p.Name).ToList());
            foreach (string label in labels)
            {
                if (label == TrayMenuBuilder.SeparatorLabel)
                {
                    trayContextMenu.Items.Add(new Forms.ToolStripSeparator());
                }
                else if (label == TrayMenuBuilder.OpenDashboardLabel)
                {
                    trayContextMenu.Items.Add("Open Dashboard", null, (s, e) => ShowDashboard());
                }
                else if (label == TrayMenuBuilder.ExitLabel)
                {
                    trayContextMenu.Items.Add("Exit App", null, (s, e) =>
                    {
                        isExiting = true;
                        System.Windows.Application.Current.Shutdown();
                    });
                }
                else if (label == TrayMenuBuilder.NoPresetsLabel)
                {
                    trayContextMenu.Items.Add(new Forms.ToolStripMenuItem(TrayMenuBuilder.NoPresetsLabel) { Enabled = false });
                }
                else
                {
                    ScalingPreset? preset = presets.FirstOrDefault(p => p.Name == label);
                    if (preset is null)
                        continue;
                    var item = new Forms.ToolStripMenuItem(preset.Name);
                    item.Click += (_, _) => ApplyPreset(preset);
                    trayContextMenu.Items.Add(item);
                }
            }
        }

        // ---------- Monitors ----------

        private void LoadConnectedMonitors(Dictionary<string, bool>? preserveSelection = null)
        {
            MonitorCanvas.Children.Clear();
            detectedMonitors.Clear();

            Dictionary<string, DisplayScaleInfo> detailsByDevice = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (DisplayScaleInfo detail in ScaleManager.GetActiveDisplays())
                    detailsByDevice[detail.DeviceName] = detail;
            }
            catch
            {
                // Details are best-effort; Screen info below is the fallback.
            }

            Forms.Screen[] screens;
            try
            {
                screens = Forms.Screen.AllScreens;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Could not detect displays: {ex.Message}";
                return;
            }

            for (int i = 0; i < screens.Length; i++)
            {
                Forms.Screen screen = screens[i];
                detailsByDevice.TryGetValue(screen.DeviceName, out DisplayScaleInfo? detail);

                int? currentScale = detail?.CurrentScalePercentage;
                if (currentScale is null && ScaleManager.TryGetCurrentScale(screen.DeviceName, out int scale))
                    currentScale = scale;

                var info = new MonitorDisplayInfo
                {
                    DeviceName = screen.DeviceName,
                    FriendlyName = !string.IsNullOrWhiteSpace(detail?.FriendlyName)
                        ? detail!.FriendlyName!
                        : $"Monitor {i + 1}",
                    Bounds = screen.Bounds,
                    CurrentScalePercentage = currentScale,
                    NativeWidth = detail?.NativeWidth,
                    NativeHeight = detail?.NativeHeight,
                    IsSelected = preserveSelection is not null
                        ? preserveSelection.TryGetValue(screen.DeviceName, out bool wasSelected) && wasSelected
                        : true
                };
                detectedMonitors.Add(info);

                var nameLabel = new TextBlock
                {
                    Foreground = WpfMedia.Brushes.White,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };
                var detailsLabel = new TextBlock
                {
                    Foreground = WpfMedia.Brushes.White,
                    FontSize = 11,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };
                info.NameLabel = nameLabel;
                info.DetailsLabel = detailsLabel;

                var card = new ToggleButton
                {
                    IsChecked = info.IsSelected,
                    BorderBrush = WpfMedia.Brushes.White,
                    BorderThickness = new Thickness(2),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = info,
                    ToolTip = "Toggle selection (Space / click). Double-click applies the scale below to this display only.",
                    Padding = new Thickness(6)
                };
                System.Windows.Automation.AutomationProperties.SetName(card, info.FriendlyName);
                info.Card = card;

                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(nameLabel);
                stack.Children.Add(detailsLabel);
                card.Content = stack;

                card.Checked += (_, _) => { info.IsSelected = true; UpdateMonitorCard(info); };
                card.Unchecked += (_, _) => { info.IsSelected = false; UpdateMonitorCard(info); };
                card.MouseDoubleClick += (_, _) =>
                {
                    if (TryGetSelectedScale(out int scalePercent))
                        ExecuteScaleChange(scalePercent, [info], [], $"Monitor {info.FriendlyName}");
                };

                MonitorCanvas.Children.Add(card);
                UpdateMonitorCard(info);
            }

            LayoutMonitorCards();

            // Pre-select the combo from the first monitor's current scale (never blindly 100%).
            int? preferred = detectedMonitors.FirstOrDefault(m => m.CurrentScalePercentage.HasValue)?.CurrentScalePercentage;
            PopulateScaleCombo(preferred);
            UpdateStatusText();
        }

        private void LayoutMonitorCards()
        {
            if (detectedMonitors.Count == 0)
                return;

            double minX = detectedMonitors.Min(monitor => monitor.Bounds.X);
            double minY = detectedMonitors.Min(monitor => monitor.Bounds.Y);
            double maxX = detectedMonitors.Max(monitor => monitor.Bounds.Right);
            double maxY = detectedMonitors.Max(monitor => monitor.Bounds.Bottom);
            double totalWidth = Math.Max(1, maxX - minX);
            double totalHeight = Math.Max(1, maxY - minY);
            double availableWidth = Math.Max(1, MonitorCanvas.ActualWidth - 30);
            double availableHeight = Math.Max(1, MonitorCanvas.ActualHeight - 30);
            double scaleFactor = Math.Min(availableWidth / totalWidth, availableHeight / totalHeight);

            foreach (MonitorDisplayInfo monitor in detectedMonitors)
            {
                if (monitor.Card is not ToggleButton card)
                    continue;

                card.Width = Math.Max(110, monitor.Bounds.Width * scaleFactor);
                card.Height = Math.Max(64, monitor.Bounds.Height * scaleFactor);
                Canvas.SetLeft(card, (monitor.Bounds.X - minX) * scaleFactor + 15);
                Canvas.SetTop(card, (monitor.Bounds.Y - minY) * scaleFactor + 15);
            }
        }

        private static void UpdateMonitorCard(MonitorDisplayInfo monitor)
        {
            if (monitor.Card is not ToggleButton card)
                return;

            card.Background = monitor.IsSelected
                ? new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0, 122, 204))
                : new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(160, 160, 160));
            card.BorderThickness = new Thickness(monitor.IsSelected ? 3 : 2);

            string size = monitor.NativeWidth.HasValue && monitor.NativeHeight.HasValue
                ? $"{monitor.NativeWidth}×{monitor.NativeHeight} native"
                : $"{monitor.Bounds.Width}×{monitor.Bounds.Height}";
            string scale = monitor.CurrentScalePercentage.HasValue
                ? $"now {monitor.CurrentScalePercentage}%"
                : "scale unknown";
            string state = monitor.IsSelected ? "✓ selected" : "not selected";

            if (monitor.NameLabel is not null)
                monitor.NameLabel.Text = monitor.FriendlyName;
            if (monitor.DetailsLabel is not null)
                monitor.DetailsLabel.Text = $"{size}\n{scale} · {state}";

            card.ToolTip = $"{monitor.FriendlyName} — {scale}, {state}. Click/Space toggles; double-click applies to this display only.";
        }

        private void RefreshMonitorScales()
        {
            foreach (MonitorDisplayInfo monitor in detectedMonitors)
            {
                if (ScaleManager.TryGetCurrentScale(monitor.DeviceName, out int scale))
                    monitor.CurrentScalePercentage = scale;
                UpdateMonitorCard(monitor);
            }

            UpdateStatusText();
        }

        private void UpdateStatusText(string? extra = null)
        {
            if (detectedMonitors.Count == 0)
            {
                StatusText.Text = extra ?? "No displays detected.";
                return;
            }

            string current = string.Join("  ·  ", detectedMonitors.Select(m =>
                $"{m.FriendlyName}: {(m.CurrentScalePercentage.HasValue ? m.CurrentScalePercentage + "%" : "unknown")}"));
            StatusText.Text = extra is null ? $"Current: {current}" : $"{extra}  |  Current: {current}";
        }

        // ---------- Scale combo ----------

        private void PopulateScaleCombo(int? preferredScale = null)
        {
            CmbScaleTarget.Items.Clear();
            ComboBoxItem? selected = null;
            foreach (int pct in ScaleManager.SupportedScalePercentages)
            {
                var item = new ComboBoxItem
                {
                    Content = pct >= 350 ? $"⚠ {pct}%" : $"{pct}%",
                    Tag = pct
                };
                CmbScaleTarget.Items.Add(item);
                if (preferredScale == pct)
                    selected = item;
            }

            if (selected is not null)
                CmbScaleTarget.SelectedItem = selected;
            else if (CmbScaleTarget.Items.Count > 0)
                CmbScaleTarget.SelectedIndex = 0;
        }

        private bool TryGetSelectedScale(out int scalePercent)
        {
            scalePercent = 100;
            if (CmbScaleTarget.SelectedItem is ComboBoxItem item)
            {
                if (item.Tag is int tagged)
                {
                    scalePercent = tagged;
                    return true;
                }

                string digits = new string((item.Content?.ToString() ?? "").Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out scalePercent))
                    return true;
            }

            ShowUserMessage("Invalid scale", "Select a valid scaling percentage.", MessageBoxImage.Warning);
            return false;
        }

        // ---------- Applying scaling ----------

        private void BtnApplyScale_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetSelectedScale(out int scalePercent))
                return;
            List<MonitorDisplayInfo> targets = detectedMonitors.Where(m => m.IsSelected).ToList();
            ExecuteScaleChange(scalePercent, targets, []);
        }

        private void BtnApplyPreset_Click(object sender, RoutedEventArgs e)
        {
            if (PresetsListBox.SelectedItem is ScalingPreset selectedPreset)
                ApplyPreset(selectedPreset);
            else
                ShowUserMessage("No preset selected", "Select a preset from the list, or use “Apply to Selected” with the Target Scale.", MessageBoxImage.Information);
        }

        private void PresetsListBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && PresetsListBox.SelectedItem is ScalingPreset preset)
            {
                ApplyPreset(preset);
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Delete)
            {
                BtnDeletePreset_Click(sender, e);
                e.Handled = true;
            }
        }

        private void BtnDesk_Click(object sender, RoutedEventArgs e) => ApplyHeroPreset(isDesk: true);

        private void BtnCouch_Click(object sender, RoutedEventArgs e) => ApplyHeroPreset(isDesk: false);

        private void ApplyHeroPreset(bool isDesk)
        {
            string? pinnedName = isDesk ? settings.DeskPresetName : settings.CouchPresetName;
            ScalingPreset? pinned = pinnedName is null
                ? null
                : presets.FirstOrDefault(p => string.Equals(p.Name, pinnedName, StringComparison.OrdinalIgnoreCase));

            if (pinned is not null)
            {
                ApplyPreset(pinned);
                return;
            }

            int scale = isDesk ? settings.DeskScalePercent : settings.CouchScalePercent;
            ExecuteScaleChange(scale, detectedMonitors.ToList(), [], isDesk ? "Desk" : "Couch Mode");
        }

        private void ApplyPreset(ScalingPreset preset)
        {
            List<MonitorDisplayInfo> targets;
            List<string> missingMonitors;

            if (preset.ApplyToAllMonitors)
            {
                targets = detectedMonitors.ToList();
                missingMonitors = [];
            }
            else
            {
                var monitorsByName = new Dictionary<string, MonitorDisplayInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (MonitorDisplayInfo monitor in detectedMonitors)
                {
                    if (!monitorsByName.ContainsKey(monitor.DeviceName))
                        monitorsByName[monitor.DeviceName] = monitor;
                }

                targets = preset.DeviceNames
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(monitorsByName.ContainsKey)
                    .Select(deviceName => monitorsByName[deviceName])
                    .ToList();
                missingMonitors = preset.DeviceNames
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(deviceName => !monitorsByName.ContainsKey(deviceName))
                    .ToList();
            }

            ExecuteScaleChange(preset.ScalePercentage, targets, missingMonitors, preset.Name);
        }

        private void ExecuteScaleChange(
            int percentage,
            List<MonitorDisplayInfo> targets,
            List<string> missingMonitors,
            string? description = null)
        {
            if (isBusy)
                return;

            if (targets.Count == 0)
            {
                ShowUserMessage(
                    "No available target displays",
                    missingMonitors.Count == 0
                        ? "Select at least one display before applying scaling."
                        : $"The preset targets displays that are not currently connected:\n{string.Join("\n", missingMonitors)}",
                    MessageBoxImage.Information);
                return;
            }

            if (percentage >= 350 && !cliOptions.ExitAfterApply)
            {
                ShowDashboard();
                if (MessageBox.Show(
                        $"{percentage}% scaling makes everything very large and may be hard to undo. Continue?",
                        "Extreme scaling",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            SetBusy(true);
            var previousScales = new List<(string DeviceName, int ScalePercent)>();
            var failures = new List<string>();
            int appliedCount = 0;
            try
            {
                foreach (MonitorDisplayInfo monitor in targets)
                {
                    if (ScaleManager.TryGetCurrentScale(monitor.DeviceName, out int previous))
                        previousScales.Add((monitor.DeviceName, previous));

                    try
                    {
                        ScaleManager.SetMonitorScaling(monitor.DeviceName, percentage);
                        appliedCount++;
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{monitor.FriendlyName}: {ex.Message}");
                    }
                }
            }
            finally
            {
                SetBusy(false);
                lastManualApplyUtc = DateTime.UtcNow;
            }

            RefreshMonitorScales();

            if (failures.Count == 0)
            {
                string label = description ?? $"{percentage}%";
                string message = $"Saved {percentage}% scaling for {appliedCount} display(s) ({label}).";
                if (missingMonitors.Count > 0)
                    message += $" Not connected: {string.Join(", ", missingMonitors)}.";
                message += " Sign out and back in if Windows does not apply it immediately.";

                UpdateStatusText($"{label} applied.");
                if (cliOptions.ExitAfterApply)
                {
                    trayIcon.ShowBalloonTip(3000, "Scaling saved", message, Forms.ToolTipIcon.Info);
                    return;
                }

                StartRevertWindow(previousScales, percentage, label, message);
                return;
            }

            ShowUserMessage(
                "Some displays could not be updated",
                $"Saved scaling for {appliedCount} of {targets.Count} connected display(s)." +
                (missingMonitors.Count > 0 ? $"\n\nNot connected: {string.Join(", ", missingMonitors)}" : "") +
                $"\n\n{string.Join("\n", failures)}",
                MessageBoxImage.Warning);

            if (appliedCount > 0 && !cliOptions.ExitAfterApply)
                StartRevertWindow(previousScales, percentage, description ?? $"{percentage}%", "Partially applied.");
        }

        private void SetBusy(bool busy)
        {
            isBusy = busy;
            foreach (Button button in new[] { BtnDesk, BtnCouch, BtnApplyScale, BtnApplyPreset, BtnAddPreset })
            {
                if (button is not null)
                    button.IsEnabled = !busy;
            }
        }

        // ---------- Revert window ----------

        private void StartRevertWindow(
            List<(string DeviceName, int ScalePercent)> previousScales,
            int appliedPercent,
            string label,
            string message)
        {
            pendingRevert.Clear();
            foreach ((string device, int scale) in previousScales)
            {
                if (scale != appliedPercent)
                    pendingRevert.Add((device, scale));
            }

            if (pendingRevert.Count == 0)
            {
                trayIcon.ShowBalloonTip(4000, "Scaling saved", message, Forms.ToolTipIcon.Info);
                return;
            }

            if (IsVisible)
            {
                RevertPanel.Visibility = Visibility.Visible;
                RevertCountdownText.Text = $"Reverting in {RevertCountdownSeconds}s…";
                trayIcon.ShowBalloonTip(4000, "Scaling saved", message, Forms.ToolTipIcon.Info);
            }
            else
            {
                var popup = new NotificationPopup(
                    "Scaling saved — revert?",
                    $"{message}\n\nRevert to your previous scales within {RevertCountdownSeconds} seconds, or dismiss to keep {appliedPercent}%.",
                    "Revert",
                    RevertCountdownSeconds);
                popup.ActionClicked += () => RevertPending();
                popup.Show();
            }

            revertTimer.Stop();
            revertTimer.Tag = RevertCountdownSeconds;
            revertTimer.Start();
        }

        private void RevertTimer_Tick(object? sender, EventArgs e)
        {
            int secondsLeft = (int)(revertTimer.Tag ?? RevertCountdownSeconds) - 1;
            if (secondsLeft <= 0 || pendingRevert.Count == 0)
            {
                ClearPendingRevert();
                return;
            }

            revertTimer.Tag = secondsLeft;
            RevertCountdownText.Text = $"Reverting in {secondsLeft}s…";
        }

        private void BtnRevert_Click(object sender, RoutedEventArgs e) => RevertPending();

        private void BtnKeep_Click(object sender, RoutedEventArgs e)
        {
            ClearPendingRevert();
            UpdateStatusText("Kept the new scale.");
        }

        private void RevertPending()
        {
            if (pendingRevert.Count == 0 || isBusy)
                return;

            var toRestore = pendingRevert.ToList();
            ClearPendingRevert();

            SetBusy(true);
            int restored = 0;
            try
            {
                foreach ((string device, int scale) in toRestore)
                {
                    try
                    {
                        ScaleManager.SetMonitorScaling(device, scale);
                        restored++;
                    }
                    catch
                    {
                        // Best effort per display.
                    }
                }
            }
            finally
            {
                SetBusy(false);
                lastManualApplyUtc = DateTime.UtcNow;
            }

            RefreshMonitorScales();
            UpdateStatusText($"Reverted {restored} display(s) to their previous scales.");
            trayIcon.ShowBalloonTip(3000, "Reverted", $"Restored previous scales on {restored} display(s).", Forms.ToolTipIcon.Info);
        }

        private void ClearPendingRevert()
        {
            pendingRevert.Clear();
            revertTimer.Stop();
            if (RevertPanel is not null)
                RevertPanel.Visibility = Visibility.Collapsed;
        }

        // ---------- Presets ----------

        private void LoadPresets()
        {
            try
            {
                presets.Clear();
                foreach (ScalingPreset preset in PresetStore.LoadOrCreateDefault())
                    presets.Add(preset);

                if (PresetStore.RecoveredFromBackup)
                {
                    ShowUserMessage(
                        "Presets recovered",
                        "The main preset file was corrupt, so your presets were recovered from the backup copy (presets.json.bak). Save a preset to write a fresh file.",
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                presets.Clear();
                presets.Add(PresetStore.CreateDefaultPreset());
                ShowUserMessage(
                    "Preset loading error",
                    $"Saved presets could not be loaded. The original preset file was left unchanged.\n\n{ex.Message}",
                    MessageBoxImage.Error);
            }

            RefreshAutoApplyCombo();
            RefreshHeroButtons();
        }

        private void BtnAddPreset_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetSelectedScale(out int scalePercent))
                return;

            List<MonitorDisplayInfo> selectedMonitors = detectedMonitors.Where(m => m.IsSelected).ToList();
            if (selectedMonitors.Count == 0)
            {
                ShowUserMessage("No monitors selected", "Select one or more monitor cards before creating a preset.", MessageBoxImage.Information);
                return;
            }

            bool applyToAll = selectedMonitors.Count == detectedMonitors.Count;
            string customName = PresetNameTextBox.Text.Trim();
            string name = string.IsNullOrWhiteSpace(customName)
                ? PresetStore.BuildPresetName(scalePercent, selectedMonitors.Select(m => m.FriendlyName).ToList(), applyToAll)
                : customName;

            if (presets.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                ShowUserMessage("Duplicate preset name", $"A preset named “{name}” already exists. Choose a different name.", MessageBoxImage.Warning);
                return;
            }

            var preset = new ScalingPreset
            {
                Name = name,
                ScalePercentage = scalePercent,
                ApplyToAllMonitors = applyToAll,
                DeviceNames = applyToAll ? [] : selectedMonitors.Select(m => m.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };

            List<ScalingPreset> updatedPresets = presets.Append(preset).ToList();
            if (!TrySavePresets(updatedPresets))
                return;

            presets.Add(preset);
            PresetNameTextBox.Clear();
            PresetsListBox.SelectedItem = preset;
            RefreshTrayPresetMenu();
            RefreshAutoApplyCombo();
            RefreshHeroButtons();
            UpdateStatusText($"Preset “{name}” saved.");
        }

        private void BtnRenamePreset_Click(object sender, RoutedEventArgs e)
        {
            if (PresetsListBox.SelectedItem is not ScalingPreset selectedPreset)
            {
                ShowUserMessage("No preset selected", "Select a preset to rename.", MessageBoxImage.Information);
                return;
            }

            var dialog = new PromptDialog("Rename preset", "Preset name (max 120 characters):", selectedPreset.Name)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
                return;

            string newName = dialog.ResultText;
            if (string.IsNullOrWhiteSpace(newName))
                return;
            if (presets.Any(p => !ReferenceEquals(p, selectedPreset) && string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase)))
            {
                ShowUserMessage("Duplicate preset name", $"A preset named “{newName}” already exists.", MessageBoxImage.Warning);
                return;
            }

            List<ScalingPreset> updatedPresets = presets
                .Select(p => ReferenceEquals(p, selectedPreset)
                    ? new ScalingPreset { Name = newName, ScalePercentage = p.ScalePercentage, ApplyToAllMonitors = p.ApplyToAllMonitors, DeviceNames = [.. p.DeviceNames] }
                    : p)
                .ToList();
            if (!TrySavePresets(updatedPresets))
                return;

            presets.Remove(selectedPreset);
            selectedPreset.Name = newName;
            presets.Add(selectedPreset);
            PresetsListBox.SelectedItem = selectedPreset;
            RefreshTrayPresetMenu();
            RefreshAutoApplyCombo();
            RefreshHeroButtons();
            RefreshProfilesList();
        }

        private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (PresetsListBox.SelectedItem is not ScalingPreset selectedPreset)
            {
                ShowUserMessage("No preset selected", "Select a preset to delete.", MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(
                    $"Delete preset “{selectedPreset.Name}”? This cannot be undone.",
                    "Delete preset",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            List<ScalingPreset> updatedPresets = presets.Where(p => !ReferenceEquals(p, selectedPreset)).ToList();
            if (!TrySavePresets(updatedPresets))
                return;

            presets.Remove(selectedPreset);
            if (string.Equals(settings.DeskPresetName, selectedPreset.Name, StringComparison.OrdinalIgnoreCase))
                settings.DeskPresetName = null;
            if (string.Equals(settings.CouchPresetName, selectedPreset.Name, StringComparison.OrdinalIgnoreCase))
                settings.CouchPresetName = null;
            SaveSettings();
            RefreshTrayPresetMenu();
            RefreshAutoApplyCombo();
            RefreshHeroButtons();
            RefreshProfilesList();
        }

        private void PresetsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Selection is intentionally not auto-applied; explicit Apply buttons keep it unambiguous.
        }

        private bool TrySavePresets(List<ScalingPreset> updatedPresets)
        {
            try
            {
                PresetStore.Save(updatedPresets);
                return true;
            }
            catch (Exception ex)
            {
                ShowUserMessage(
                    "Preset save error",
                    $"The preset change could not be saved. Your current presets were not changed.\n\n{ex.Message}",
                    MessageBoxImage.Error);
                return false;
            }
        }

        private void BtnPinDesk_Click(object sender, RoutedEventArgs e) => PinPreset(isDesk: true);

        private void BtnPinCouch_Click(object sender, RoutedEventArgs e) => PinPreset(isDesk: false);

        private void PinPreset(bool isDesk)
        {
            if (PresetsListBox.SelectedItem is not ScalingPreset selectedPreset)
            {
                ShowUserMessage("No preset selected", "Select a preset to pin.", MessageBoxImage.Information);
                return;
            }

            if (isDesk)
                settings.DeskPresetName = selectedPreset.Name;
            else
                settings.CouchPresetName = selectedPreset.Name;
            SaveSettings();
            RefreshHeroButtons();
            UpdateStatusText($"“{selectedPreset.Name}” pinned as {(isDesk ? "Desk" : "Couch")}.");
        }

        private void RefreshHeroButtons()
        {
            string deskLabel = settings.DeskPresetName is not null
                && presets.Any(p => string.Equals(p.Name, settings.DeskPresetName, StringComparison.OrdinalIgnoreCase))
                ? $"🖥 Desk — {settings.DeskPresetName}"
                : $"🖥 Desk — {settings.DeskScalePercent}%";
            string couchLabel = settings.CouchPresetName is not null
                && presets.Any(p => string.Equals(p.Name, settings.CouchPresetName, StringComparison.OrdinalIgnoreCase))
                ? $"🛋 Couch — {settings.CouchPresetName}"
                : $"🛋 Couch — {settings.CouchScalePercent}%";
            BtnDesk.Content = deskLabel;
            BtnCouch.Content = couchLabel;
        }

        // ---------- Monitor selection ----------

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (MonitorDisplayInfo monitor in detectedMonitors)
            {
                monitor.IsSelected = true;
                if (monitor.Card is not null)
                    monitor.Card.IsChecked = true;
                UpdateMonitorCard(monitor);
            }
        }

        private void BtnSelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (MonitorDisplayInfo monitor in detectedMonitors)
            {
                monitor.IsSelected = false;
                if (monitor.Card is not null)
                    monitor.Card.IsChecked = false;
                UpdateMonitorCard(monitor);
            }
        }

        // ---------- Settings ----------

        private void ApplySettingsToControls()
        {
            suppressSettingsEvents = true;
            try
            {
                ChkMinimizeToTray.IsChecked = settings.MinimizeToTray;
                ChkHotkeys.IsChecked = settings.HotkeysEnabled;
                ChkAutoApply.IsChecked = settings.AutoApplyEnabled;
                ChkStartup.IsChecked = IsStartupEnabled();
                RefreshAutoApplyCombo();
                RefreshProfilesList();
            }
            finally
            {
                suppressSettingsEvents = false;
            }
        }

        private void SaveSettings()
        {
            try
            {
                AppSettingsStore.Save(settings);
            }
            catch (Exception ex)
            {
                ShowUserMessage("Settings save error", $"Settings could not be saved.\n\n{ex.Message}", MessageBoxImage.Error);
            }
        }

        private void ChkHotkeys_Changed(object sender, RoutedEventArgs e)
        {
            if (suppressSettingsEvents)
                return;
            settings.HotkeysEnabled = ChkHotkeys.IsChecked == true;
            SaveSettings();
            if (settings.HotkeysEnabled)
                TryRegisterHotKeys();
            else
                UnregisterHotKeys();
        }

        private void ChkAutoApply_Changed(object sender, RoutedEventArgs e)
        {
            if (suppressSettingsEvents)
                return;
            settings.AutoApplyEnabled = ChkAutoApply.IsChecked == true;
            SaveSettings();
        }

        private void RefreshAutoApplyCombo()
        {
            if (CmbAutoApplyPreset is null)
                return;
            suppressSettingsEvents = true;
            try
            {
                CmbAutoApplyPreset.Items.Clear();
                foreach (ScalingPreset preset in presets)
                    CmbAutoApplyPreset.Items.Add(preset.Name);
                if (settings.AutoApplyPresetName is not null)
                {
                    int index = CmbAutoApplyPreset.Items.IndexOf(settings.AutoApplyPresetName);
                    if (index >= 0)
                        CmbAutoApplyPreset.SelectedIndex = index;
                }
            }
            finally
            {
                suppressSettingsEvents = false;
            }
        }

        private void CmbAutoApplyPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressSettingsEvents)
                return;
            settings.AutoApplyPresetName = CmbAutoApplyPreset.SelectedItem?.ToString();
            SaveSettings();
        }

        private void RefreshProfilesList()
        {
            if (ProfilesListBox is null)
                return;
            ProfilesListBox.Items.Clear();
            foreach (KeyValuePair<string, string> profile in settings.AppProfiles.OrderBy(kv => kv.Key))
                ProfilesListBox.Items.Add($"{profile.Key} → {profile.Value}");
            if (settings.AppProfiles.Count > 0 && !foregroundTimer.IsEnabled)
                foregroundTimer.Start();
        }

        private void BtnAddProfile_Click(object sender, RoutedEventArgs e)
        {
            string process = ProfileProcessTextBox.Text.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(process))
            {
                ShowUserMessage("Missing process name", "Type a process file name such as game.exe.", MessageBoxImage.Information);
                return;
            }

            if (!process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                process += ".exe";

            if (PresetsListBox.SelectedItem is not ScalingPreset preset)
            {
                ShowUserMessage("No preset selected", "Select the preset to apply for this program, then click Add.", MessageBoxImage.Information);
                return;
            }

            settings.AppProfiles[process] = preset.Name;
            ProfileProcessTextBox.Clear();
            SaveSettings();
            RefreshProfilesList();
        }

        private void BtnRemoveProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesListBox.SelectedIndex < 0)
                return;
            List<string> keys = settings.AppProfiles.Keys.OrderBy(k => k).ToList();
            if (ProfilesListBox.SelectedIndex < keys.Count)
            {
                settings.AppProfiles.Remove(keys[ProfilesListBox.SelectedIndex]);
                SaveSettings();
                RefreshProfilesList();
            }
        }

        private void CheckForegroundProfile()
        {
            if (settings.AppProfiles.Count == 0 || isBusy || isExiting)
                return;

            string? process = GetForegroundProcessName();
            if (process is null || process == lastForegroundProcess)
                return;
            lastForegroundProcess = process;

            if (!settings.AppProfiles.TryGetValue(process, out string? presetName))
                return;
            if ((DateTime.UtcNow - lastAutoApplyUtc).TotalSeconds < 10)
                return;

            ScalingPreset? preset = presets.FirstOrDefault(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
            if (preset is not null)
            {
                lastAutoApplyUtc = DateTime.UtcNow;
                ApplyPreset(preset);
            }
        }

        private static string? GetForegroundProcessName()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                    return null;
                GetWindowThreadProcessId(hwnd, out uint pid);
                using Process process = Process.GetProcessById((int)pid);
                string name = process.ProcessName.ToLowerInvariant();
                return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
            }
            catch
            {
                return null;
            }
        }

        // ---------- Startup registry ----------

        private static bool IsStartupEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: false);
                return key?.GetValue("QuickScaler") is not null;
            }
            catch
            {
                return false;
            }
        }

        private void ChkStartup_Changed(object sender, RoutedEventArgs e)
        {
            if (suppressSettingsEvents)
                return;
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true)
                    ?? throw new InvalidOperationException("The Windows startup settings could not be opened.");

                if (ChkStartup.IsChecked == true)
                {
                    string executablePath = Environment.ProcessPath
                        ?? throw new InvalidOperationException("The application executable path could not be determined.");
                    key.SetValue("QuickScaler", $"\"{executablePath}\" --tray");
                }
                else
                {
                    key.DeleteValue("QuickScaler", false);
                }
            }
            catch (Exception ex)
            {
                ShowUserMessage("Startup setting error", $"The startup setting could not be changed.\n\n{ex.Message}", MessageBoxImage.Error);
            }
        }

        // ---------- Display changes ----------

        private void OnDisplaySettingsChangedEvent(object? sender, EventArgs e)
        {
            // Debounce: Windows can fire several events in quick succession.
            displayRefreshTimer.Stop();
            displayRefreshTimer.Start();
        }

        private void OnDisplaySettingsChanged()
        {
            if (isExiting)
                return;

            var preserve = detectedMonitors.ToDictionary(m => m.DeviceName, m => m.IsSelected, StringComparer.OrdinalIgnoreCase);
            LoadConnectedMonitors(preserve);

            if (!settings.AutoApplyEnabled || string.IsNullOrWhiteSpace(settings.AutoApplyPresetName))
                return;
            if ((DateTime.UtcNow - lastManualApplyUtc).TotalSeconds < 5)
                return;
            if ((DateTime.UtcNow - lastAutoApplyUtc).TotalSeconds < 10)
                return;

            ScalingPreset? preset = presets.FirstOrDefault(p =>
                string.Equals(p.Name, settings.AutoApplyPresetName, StringComparison.OrdinalIgnoreCase));
            if (preset is not null)
            {
                lastAutoApplyUtc = DateTime.UtcNow;
                ApplyPreset(preset);
            }
        }

        // ---------- Hotkeys ----------

        private bool TryRegisterHotKeys()
        {
            if (hotkeysRegistered || hotkeyWindowHandle == IntPtr.Zero)
                return false;

            bool deskOk = RegisterHotKey(hotkeyWindowHandle, HotkeyDesk, MOD_CONTROL | MOD_ALT, VK_D);
            bool couchOk = RegisterHotKey(hotkeyWindowHandle, HotkeyCouch, MOD_CONTROL | MOD_ALT, VK_C);
            bool upOk = RegisterHotKey(hotkeyWindowHandle, HotkeyScaleUp, MOD_CONTROL | MOD_ALT, VK_UP);
            bool downOk = RegisterHotKey(hotkeyWindowHandle, HotkeyScaleDown, MOD_CONTROL | MOD_ALT, VK_DOWN);

            hotkeysRegistered = deskOk || couchOk || upOk || downOk;
            if (!deskOk || !couchOk || !upOk || !downOk)
            {
                UpdateStatusText("A hotkey is already in use by another app; registered the rest.");
            }

            return hotkeysRegistered;
        }

        private void UnregisterHotKeys()
        {
            if (!hotkeysRegistered || hotkeyWindowHandle == IntPtr.Zero)
            {
                hotkeysRegistered = false;
                return;
            }

            UnregisterHotKey(hotkeyWindowHandle, HotkeyDesk);
            UnregisterHotKey(hotkeyWindowHandle, HotkeyCouch);
            UnregisterHotKey(hotkeyWindowHandle, HotkeyScaleUp);
            UnregisterHotKey(hotkeyWindowHandle, HotkeyScaleDown);
            hotkeysRegistered = false;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                handled = true;
                switch (wParam.ToInt32())
                {
                    case HotkeyDesk:
                        ApplyHeroPreset(isDesk: true);
                        break;
                    case HotkeyCouch:
                        ApplyHeroPreset(isDesk: false);
                        break;
                    case HotkeyScaleUp:
                        StepScale(direction: +1);
                        break;
                    case HotkeyScaleDown:
                        StepScale(direction: -1);
                        break;
                }
            }

            return IntPtr.Zero;
        }

        private void StepScale(int direction)
        {
            List<MonitorDisplayInfo> targets = detectedMonitors.Where(m => m.IsSelected).ToList();
            if (targets.Count == 0)
                targets = detectedMonitors.ToList();
            if (targets.Count == 0)
                return;

            int baseScale = targets.FirstOrDefault(m => m.CurrentScalePercentage.HasValue)?.CurrentScalePercentage ?? 100;
            int? target = direction > 0 ? ScaleManager.NextScale(baseScale) : ScaleManager.PreviousScale(baseScale);
            if (target is not int scale)
                return;

            ExecuteScaleChange(scale, targets, [], direction > 0 ? "Scale up" : "Scale down");
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        // ---------- CLI ----------

        private void HandleCliOptions()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(cliOptions.ExportPath))
                {
                    PresetStore.ExportTo(cliOptions.ExportPath, presets);
                    UpdateStatusText($"Presets exported to {cliOptions.ExportPath}.");
                }

                if (!string.IsNullOrWhiteSpace(cliOptions.ImportPath))
                {
                    List<ScalingPreset> imported = PresetStore.ImportFrom(cliOptions.ImportPath);
                    presets.Clear();
                    foreach (ScalingPreset preset in imported)
                        presets.Add(preset);
                    RefreshTrayPresetMenu();
                    RefreshAutoApplyCombo();
                    RefreshHeroButtons();
                    UpdateStatusText($"Imported {imported.Count} preset(s).");
                }

                bool applied = false;
                if (cliOptions.Desk)
                {
                    ApplyHeroPreset(isDesk: true);
                    applied = true;
                }
                else if (cliOptions.Couch)
                {
                    ApplyHeroPreset(isDesk: false);
                    applied = true;
                }
                else if (!string.IsNullOrWhiteSpace(cliOptions.PresetName))
                {
                    ScalingPreset? preset = presets.FirstOrDefault(p =>
                        string.Equals(p.Name, cliOptions.PresetName, StringComparison.OrdinalIgnoreCase));
                    if (preset is not null)
                    {
                        ApplyPreset(preset);
                        applied = true;
                    }
                    else
                    {
                        ShowUserMessage("Preset not found", $"No saved preset named “{cliOptions.PresetName}”.", MessageBoxImage.Warning);
                    }
                }
                else if (cliOptions.ScalePercent.HasValue)
                {
                    List<MonitorDisplayInfo> targets;
                    if (cliOptions.DeviceNames.Count > 0)
                    {
                        targets = cliOptions.DeviceNames
                            .SelectMany(name => detectedMonitors.Where(m => string.Equals(m.DeviceName, name, StringComparison.OrdinalIgnoreCase)))
                            .Distinct()
                            .ToList();
                    }
                    else if (cliOptions.MonitorIndexes.Count > 0)
                    {
                        targets = cliOptions.MonitorIndexes
                            .Where(index => index >= 1 && index <= detectedMonitors.Count)
                            .Select(index => detectedMonitors[index - 1])
                            .Distinct()
                            .ToList();
                    }
                    else
                    {
                        targets = detectedMonitors.ToList();
                    }

                    ExecuteScaleChange(cliOptions.ScalePercent.Value, targets, []);
                    applied = true;
                }

                if (cliOptions.StartMinimized && !cliOptions.ExitAfterApply)
                {
                    Hide();
                }

                if (cliOptions.ExitAfterApply && (applied || cliOptions.ExportPath is not null || cliOptions.ImportPath is not null))
                {
                    isExiting = true;
                    System.Windows.Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                ShowUserMessage("Command-line error", ex.Message, MessageBoxImage.Error);
                if (cliOptions.ExitAfterApply)
                {
                    isExiting = true;
                    System.Windows.Application.Current.Shutdown();
                }
            }
        }

        // ---------- First run ----------

        private void ShowFirstRunWizard()
        {
            string summary = detectedMonitors.Count == 0
                ? "unknown"
                : string.Join(", ", detectedMonitors.Select(m => $"{m.FriendlyName} {(m.CurrentScalePercentage.HasValue ? m.CurrentScalePercentage + "%" : "?")}"));

            var wizard = new FirstRunWindow(detectedMonitors.Count, summary) { Owner = this };
            bool finish = wizard.ShowDialog() == true;

            if (finish)
            {
                var newPresets = presets.ToList();
                if (wizard.CreateDeskPreset && !presets.Any(p => string.Equals(p.Name, "Desk — 125% — All monitors", StringComparison.OrdinalIgnoreCase)))
                {
                    var desk = new ScalingPreset { Name = "Desk — 125% — All monitors", ScalePercentage = settings.DeskScalePercent, ApplyToAllMonitors = true };
                    newPresets.Add(desk);
                    settings.DeskPresetName = desk.Name;
                }

                if (wizard.CreateCouchPreset && !presets.Any(p => string.Equals(p.Name, "Couch — 200% — All monitors", StringComparison.OrdinalIgnoreCase)))
                {
                    var couch = new ScalingPreset { Name = "Couch — 200% — All monitors", ScalePercentage = settings.CouchScalePercent, ApplyToAllMonitors = true };
                    newPresets.Add(couch);
                    settings.CouchPresetName = couch.Name;
                }

                if (newPresets.Count != presets.Count && TrySavePresets(newPresets))
                {
                    presets.Clear();
                    foreach (ScalingPreset preset in newPresets)
                        presets.Add(preset);
                    RefreshTrayPresetMenu();
                    RefreshAutoApplyCombo();
                }

                settings.HotkeysEnabled = wizard.EnableHotkeys;
                if (wizard.EnableStartup)
                {
                    try
                    {
                        using RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true)
                            ?? throw new InvalidOperationException("Startup settings could not be opened.");
                        string executablePath = Environment.ProcessPath ?? "";
                        key.SetValue("QuickScaler", $"\"{executablePath}\" --tray");
                    }
                    catch
                    {
                        // Non-fatal: the checkbox in the dashboard reflects the real state.
                    }
                }
            }

            settings.FirstRunCompleted = true;
            SaveSettings();
            ApplySettingsToControls();
            RefreshHeroButtons();
        }

        // ---------- Window ----------

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!isExiting && ChkMinimizeToTray.IsChecked == true)
            {
                e.Cancel = true;
                Hide();
                trayIcon.ShowBalloonTip(3000, "Quick Scaler", "Minimized to tray. Right-click the tray icon for presets, or use Ctrl+Alt+D / Ctrl+Alt+C.", Forms.ToolTipIcon.Info);
            }
        }

        private void ShowUserMessage(string title, string message, MessageBoxImage icon)
        {
            if (IsVisible)
            {
                MessageBox.Show(this, message, title, MessageBoxButton.OK, icon);
            }
            else
            {
                trayIcon.ShowBalloonTip(6000, title, message, icon == MessageBoxImage.Error ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Info);
            }
        }
    }

    /// <summary>
    /// A connected display and its dashboard selection state.
    /// </summary>
    public class MonitorDisplayInfo
    {
        public string DeviceName { get; set; } = "";
        public string FriendlyName { get; set; } = "";
        public Draw.Rectangle Bounds { get; set; }
        public bool IsSelected { get; set; }
        public ToggleButton? Card { get; set; }
        public TextBlock? NameLabel { get; set; }
        public TextBlock? DetailsLabel { get; set; }
        public int? CurrentScalePercentage { get; set; }
        public int? NativeWidth { get; set; }
        public int? NativeHeight { get; set; }
    }
}
