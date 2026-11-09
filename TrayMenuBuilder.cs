namespace QuickScaler
{
    /// <summary>
    /// Pure description of the tray menu order, so the ordering logic can be unit-tested
    /// without creating WinForms controls. MainWindow builds the real menu from the same order.
    /// </summary>
    public static class TrayMenuBuilder
    {
        public const string OpenDashboardLabel = "Open Dashboard";
        public const string ExitLabel = "Exit App";
        public const string NoPresetsLabel = "No saved presets";
        public const string SeparatorLabel = "---separator---";

        public static List<string> BuildLabels(IReadOnlyList<string> presetNames)
        {
            var labels = new List<string> { OpenDashboardLabel, SeparatorLabel };
            if (presetNames is null || presetNames.Count == 0)
            {
                labels.Add(NoPresetsLabel);
            }
            else
            {
                labels.AddRange(presetNames);
            }

            labels.Add(SeparatorLabel);
            labels.Add(ExitLabel);
            return labels;
        }
    }
}
