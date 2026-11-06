namespace WindowsQuickScaler
{
    /// <summary>Parsed command-line options. See README for the full list.</summary>
    public sealed class CliOptions
    {
        public bool Help { get; set; }
        public bool StartMinimized { get; set; }
        public bool ExitAfterApply { get; set; }
        public bool Desk { get; set; }
        public bool Couch { get; set; }
        public string? PresetName { get; set; }
        public int? ScalePercent { get; set; }
        public bool ApplyToAll { get; set; }
        /// <summary>1-based monitor indexes from --monitor.</summary>
        public List<int> MonitorIndexes { get; set; } = [];
        public List<string> DeviceNames { get; set; } = [];
        public string? ExportPath { get; set; }
        public string? ImportPath { get; set; }
        public bool HasApplyAction => Desk || Couch || PresetName is not null || ScalePercent.HasValue;
    }

    /// <summary>Pure, testable command-line parser (no side effects).</summary>
    public static class CliArgs
    {
        public const string HelpText =
            "Windows Quick Scaler\n" +
            "Usage: WindowsQuickScaler.exe [options]\n" +
            "\n" +
            "  (no options)                 Open the dashboard\n" +
            "  --tray                       Start minimized to the system tray\n" +
            "  --preset \"Name\"             Apply a saved preset\n" +
            "  --desk / --couch             Apply the pinned Desk / Couch preset (or 125% / 200% on all)\n" +
            "  --scale 200 [--all]          Apply a scale percentage\n" +
            "  --scale 200 --monitor 1,2    Apply to 1-based monitor numbers\n" +
            "  --scale 200 --device \"\\\\\\\\.\\DISPLAY1\"  Apply to a GDI device name\n" +
            "  --exit                       Apply (if requested) then exit\n" +
            "  --export presets.json        Export presets to a file\n" +
            "  --import presets.json        Import presets from a file\n" +
            "  --help                       Show this help\n" +
            "\nHotkeys (when enabled): Ctrl+Alt+D Desk, Ctrl+Alt+C Couch, Ctrl+Alt+Up/Down step scale.\n";

        public static CliOptions Parse(string[] args)
        {
            var options = new CliOptions();
            if (args is null)
                return options;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string lower = arg.ToLowerInvariant();

                string? Next() => i + 1 < args.Length ? args[++i] : null;

                switch (lower)
                {
                    case "--help":
                    case "-h":
                    case "/?":
                        options.Help = true;
                        break;
                    case "--tray":
                    case "--minimized":
                        options.StartMinimized = true;
                        break;
                    case "--exit":
                        options.ExitAfterApply = true;
                        break;
                    case "--desk":
                        options.Desk = true;
                        break;
                    case "--couch":
                        options.Couch = true;
                        break;
                    case "--all":
                        options.ApplyToAll = true;
                        break;
                    case "--preset":
                        options.PresetName = Next();
                        break;
                    case "--scale":
                        if (int.TryParse(Next(), out int scale))
                            options.ScalePercent = scale;
                        break;
                    case "--monitor":
                        string? monitors = Next();
                        if (!string.IsNullOrWhiteSpace(monitors))
                        {
                            foreach (string part in monitors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            {
                                if (int.TryParse(part, out int index) && index > 0)
                                    options.MonitorIndexes.Add(index);
                            }
                        }
                        break;
                    case "--device":
                        string? device = Next();
                        if (!string.IsNullOrWhiteSpace(device))
                            options.DeviceNames.Add(device);
                        break;
                    case "--export":
                        options.ExportPath = Next();
                        break;
                    case "--import":
                        options.ImportPath = Next();
                        break;
                    default:
                        // Unknown arguments are ignored for forward compatibility.
                        break;
                }
            }

            return options;
        }
    }
}
