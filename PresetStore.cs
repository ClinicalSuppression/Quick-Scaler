using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickScaler
{
    /// <summary>
    /// A named scaling action and the displays to which it applies.
    /// </summary>
    public sealed class ScalingPreset
    {
        /// <summary>Gets or sets the display name shown in the dashboard and tray menu.</summary>
        public string Name { get; set; } = "";

        /// <summary>Gets or sets the requested scale percentage.</summary>
        public int ScalePercentage { get; set; }

        /// <summary>Gets or sets whether the preset applies to every connected display.</summary>
        public bool ApplyToAllMonitors { get; set; }

        /// <summary>Gets or sets the GDI display names targeted when not applying to all displays.</summary>
        public List<string> DeviceNames { get; set; } = [];
    }

    /// <summary>On-disk wrapper so the preset schema can evolve. Legacy plain-array files still load.</summary>
    internal sealed class PresetFile
    {
        public int Version { get; set; } = 2;
        public List<ScalingPreset> Presets { get; set; } = [];
    }

    /// <summary>
    /// Loads and atomically saves the user's presets in their local application data directory.
    /// </summary>
    internal static class PresetStore
    {
        internal const int CurrentVersion = 2;

        private static readonly string DefaultPresetFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickScaler",
            "presets.json");

        /// <summary>Override used by tests. When null, the real per-user path is used.</summary>
        internal static string? PresetFilePathOverride { get; set; }

        internal static string PresetFilePath => PresetFilePathOverride ?? DefaultPresetFilePath;

        internal static string BackupFilePath => PresetFilePath + ".bak";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>True when the last load had to recover from the .bak copy.</summary>
        internal static bool RecoveredFromBackup { get; private set; }

        /// <summary>
        /// Loads and validates saved presets, creating the default all-displays preset on first run.
        /// If the main file is corrupt but a valid .bak exists, the backup is returned and
        /// <see cref="RecoveredFromBackup"/> is set; the corrupt file is left untouched.
        /// </summary>
        public static List<ScalingPreset> LoadOrCreateDefault()
        {
            RecoveredFromBackup = false;
            if (!File.Exists(PresetFilePath))
            {
                List<ScalingPreset> defaults = [CreateDefaultPreset()];
                Save(defaults);
                return defaults;
            }

            try
            {
                return ReadPresets(PresetFilePath);
            }
            catch (Exception primaryError) when (primaryError is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                if (File.Exists(BackupFilePath))
                {
                    try
                    {
                        List<ScalingPreset> recovered = ReadPresets(BackupFilePath);
                        RecoveredFromBackup = true;
                        return recovered;
                    }
                    catch
                    {
                        // Fall through and report the primary error.
                    }
                }

                if (primaryError is JsonException)
                {
                    throw new InvalidDataException(
                        $"The saved preset file is invalid and was not changed: {PresetFilePath}",
                        primaryError);
                }

                throw;
            }
        }

        private static List<ScalingPreset> ReadPresets(string path)
        {
            string json = File.ReadAllText(path);
            List<ScalingPreset> presets = ParsePresets(json);
            Validate(presets);
            return presets;
        }

        internal static List<ScalingPreset> ParsePresets(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("The preset file is empty.");

            string trimmed = json.TrimStart();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                // Legacy format: a bare array of presets.
                return JsonSerializer.Deserialize<List<ScalingPreset>>(json, JsonOptions)
                    ?? throw new InvalidDataException("The preset file does not contain a preset list.");
            }

            PresetFile file = JsonSerializer.Deserialize<PresetFile>(json, JsonOptions)
                ?? throw new InvalidDataException("The preset file does not contain a preset list.");
            return file.Presets
                ?? throw new InvalidDataException("The preset file does not contain a preset list.");
        }

        /// <summary>Saves a validated preset collection without partially replacing the current file.</summary>
        public static void Save(IReadOnlyCollection<ScalingPreset> presets)
        {
            Validate(presets);

            string directory = Path.GetDirectoryName(PresetFilePath)
                ?? throw new InvalidOperationException("Could not determine the preset storage directory.");
            Directory.CreateDirectory(directory);

            // Keep the previous good file as a recovery copy.
            try
            {
                if (File.Exists(PresetFilePath))
                    File.Copy(PresetFilePath, BackupFilePath, overwrite: true);
            }
            catch
            {
                // A backup failure must not block saving.
            }

            var file = new PresetFile
            {
                Version = CurrentVersion,
                Presets = presets.Select(p => new ScalingPreset
                {
                    Name = p.Name,
                    ScalePercentage = p.ScalePercentage,
                    ApplyToAllMonitors = p.ApplyToAllMonitors,
                    DeviceNames = p.ApplyToAllMonitors ? [] : p.DeviceNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                }).ToList()
            };

            string temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
            try
            {
                string json = JsonSerializer.Serialize(file, JsonOptions);
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, PresetFilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        /// <summary>Exports presets to a shareable JSON file (same format as the store file).</summary>
        public static void ExportTo(string destinationPath, IReadOnlyCollection<ScalingPreset> presets)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
            Validate(presets);
            var file = new PresetFile { Version = CurrentVersion, Presets = presets.ToList() };
            string directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath))
                ?? throw new InvalidOperationException("Could not determine the export directory.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(destinationPath, JsonSerializer.Serialize(file, JsonOptions));
        }

        /// <summary>Imports presets from a file, validates them, saves them, and returns them.</summary>
        public static List<ScalingPreset> ImportFrom(string sourcePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("The import file could not be found.", sourcePath);

            List<ScalingPreset> imported = ParsePresets(File.ReadAllText(sourcePath));
            Validate(imported);
            Save(imported);
            return imported;
        }

        /// <summary>Creates the initial 100 percent preset for all connected displays.</summary>
        public static ScalingPreset CreateDefaultPreset() => new()
        {
            Name = "100% — All monitors",
            ScalePercentage = 100,
            ApplyToAllMonitors = true
        };

        /// <summary>Builds the automatic preset name used when the user does not type one.</summary>
        public static string BuildPresetName(
            int scalePercent,
            IReadOnlyCollection<string> targetLabels,
            bool applyToAll)
        {
            string targets = applyToAll
                ? "All monitors"
                : string.Join(", ", targetLabels);
            if (string.IsNullOrWhiteSpace(targets))
                targets = "All monitors";
            return $"{scalePercent}% — {targets}";
        }

        internal static void Validate(IReadOnlyCollection<ScalingPreset>? presets)
        {
            if (presets is null)
                throw new InvalidDataException("The preset list is missing.");

            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ScalingPreset? preset in presets)
            {
                if (preset is null)
                    throw new InvalidDataException("One or more saved presets are empty (null).");
                if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 120)
                    throw new InvalidDataException("One or more saved presets contain invalid names.");
                if (!seenNames.Add(preset.Name.Trim()))
                    throw new InvalidDataException($"Duplicate preset name: '{preset.Name}'. Rename one of them.");
                if (!ScaleManager.IsSupportedScale(preset.ScalePercentage))
                    throw new InvalidDataException($"Preset '{preset.Name}' has an unsupported scale ({preset.ScalePercentage}%).");
                if (preset.DeviceNames is null)
                    throw new InvalidDataException($"Preset '{preset.Name}' has no monitor list.");
                if (!preset.ApplyToAllMonitors && preset.DeviceNames.Count == 0)
                    throw new InvalidDataException($"Preset '{preset.Name}' does not target any monitor.");
                if (preset.DeviceNames.Any(string.IsNullOrWhiteSpace))
                    throw new InvalidDataException($"Preset '{preset.Name}' contains an empty monitor name.");
                if (preset.DeviceNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != preset.DeviceNames.Count)
                    throw new InvalidDataException($"Preset '{preset.Name}' lists a monitor more than once.");
            }
        }
    }
}
