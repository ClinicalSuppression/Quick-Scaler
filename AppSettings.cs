using System.IO;
using System.Text.Json;

namespace QuickScaler
{
    /// <summary>User preferences persisted next to presets.json.</summary>
    public sealed class AppSettings
    {
        public bool MinimizeToTray { get; set; } = true;
        public bool HotkeysEnabled { get; set; } = true;
        public bool AutoApplyEnabled { get; set; }
        public string? AutoApplyPresetName { get; set; }
        public int DeskScalePercent { get; set; } = 125;
        public int CouchScalePercent { get; set; } = 200;
        public string? DeskPresetName { get; set; }
        public string? CouchPresetName { get; set; }
        /// <summary>Foreground process name (e.g. "game.exe") -> preset name.</summary>
        public Dictionary<string, string> AppProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool FirstRunCompleted { get; set; }
    }

    /// <summary>Atomic JSON persistence for <see cref="AppSettings"/>.</summary>
    internal static class AppSettingsStore
    {
        private static readonly string DefaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickScaler",
            "settings.json");

        internal static string? SettingsFilePathOverride { get; set; }
        internal static string SettingsFilePath => SettingsFilePathOverride ?? DefaultPath;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                    return new AppSettings();
                string json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                settings.Normalize();
                return settings;
            }
            catch
            {
                // Corrupt settings fall back to defaults rather than blocking startup.
                return new AppSettings();
            }
        }

        public static void Save(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            settings.Normalize();
            string directory = Path.GetDirectoryName(SettingsFilePath)
                ?? throw new InvalidOperationException("Could not determine the settings directory.");
            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
                File.Move(temporaryPath, SettingsFilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        internal static void Normalize(this AppSettings settings)
        {
            if (!ScaleManager.IsSupportedScale(settings.DeskScalePercent))
                settings.DeskScalePercent = 125;
            if (!ScaleManager.IsSupportedScale(settings.CouchScalePercent))
                settings.CouchScalePercent = 200;
            settings.AppProfiles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Drop profile entries with blank keys/values instead of failing the whole file.
            foreach (string key in settings.AppProfiles.Keys.Where(string.IsNullOrWhiteSpace).ToList())
                settings.AppProfiles.Remove(key);
        }
    }
}
