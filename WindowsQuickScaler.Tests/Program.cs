// Dependency-free test harness: each Check() throws on failure; Main returns non-zero.
// Run with: dotnet run --project WindowsQuickScaler.Tests -c Release
using System.IO;
using WindowsQuickScaler;

int passed = 0;
int failed = 0;

void Check(string name, bool condition)
{
    if (condition)
    {
        passed++;
        Console.WriteLine($"  PASS  {name}");
    }
    else
    {
        failed++;
        Console.WriteLine($"  FAIL  {name}");
    }
}

void Throws<T>(string name, Action action) where T : Exception
{
    try
    {
        action();
        Check(name, false);
    }
    catch (T)
    {
        Check(name, true);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"       (threw {ex.GetType().Name} instead of {typeof(T).Name})");
        Check(name, false);
    }
}

ScalingPreset Valid(string name = "Desk", int scale = 125) => new()
{
    Name = name,
    ScalePercentage = scale,
    ApplyToAllMonitors = true
};

Console.WriteLine("ScaleManager");
Check("100 is supported", ScaleManager.IsSupportedScale(100));
Check("500 is supported", ScaleManager.IsSupportedScale(500));
Check("137 is not supported", !ScaleManager.IsSupportedScale(137));
Check("Index of 200 is 4", ScaleManager.ScaleIndex(200) == 4);
Check("Next after 200 is 225", ScaleManager.NextScale(200) == 225);
Check("Previous before 125 is 100", ScaleManager.PreviousScale(125) == 100);
Check("No scale above 500", ScaleManager.NextScale(500) is null);
Check("No scale below 100", ScaleManager.PreviousScale(100) is null);
Check("TryGetCurrentScale never throws on bad input", !ScaleManager.TryGetCurrentScale("", out _));
Check("TryGetDisplayDetails never throws on bad input", !ScaleManager.TryGetDisplayDetails("   ", out _));

Console.WriteLine("PresetStore.Validate");
PresetStore.Validate([Valid()]);
Check("valid preset passes", true);
Throws<InvalidDataException>("null preset element rejected", () => PresetStore.Validate([null!]));
Throws<InvalidDataException>("null list rejected", () => PresetStore.Validate(null));
Throws<InvalidDataException>("blank name rejected", () => PresetStore.Validate([Valid("  ")]));
Throws<InvalidDataException>("unsupported scale rejected", () => PresetStore.Validate([Valid(scale: 133)]));
Throws<InvalidDataException>("duplicate names rejected", () => PresetStore.Validate([Valid("Same"), Valid("same", 150)]));
Throws<InvalidDataException>("specific-monitor preset with no targets rejected", () =>
    PresetStore.Validate([new ScalingPreset { Name = "X", ScalePercentage = 100, ApplyToAllMonitors = false, DeviceNames = [] }]));
Throws<InvalidDataException>("duplicate device names rejected", () =>
    PresetStore.Validate([new ScalingPreset { Name = "X", ScalePercentage = 100, ApplyToAllMonitors = false, DeviceNames = [@"\\.\DISPLAY1", @"\\.\display1"] }]));
Check("preset name builder (all)", PresetStore.BuildPresetName(200, [], true) == "200% — All monitors");
Check("preset name builder (some)", PresetStore.BuildPresetName(150, ["TV"], false) == "150% — TV");

Console.WriteLine("PresetStore save/load (temp dir)");
string tempDir = Path.Combine(Path.GetTempPath(), "WQS-Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDir);
PresetStore.PresetFilePathOverride = Path.Combine(tempDir, "presets.json");
AppSettingsStore.SettingsFilePathOverride = Path.Combine(tempDir, "settings.json");
try
{
    List<ScalingPreset> firstRun = PresetStore.LoadOrCreateDefault();
    Check("first run creates the default preset", firstRun.Count == 1 && firstRun[0].ScalePercentage == 100 && firstRun[0].ApplyToAllMonitors);
    Check("first run writes the file", File.Exists(PresetStore.PresetFilePath));

    PresetStore.Save([Valid("Desk"), Valid("Couch", 200)]);
    List<ScalingPreset> loaded = PresetStore.LoadOrCreateDefault();
    Check("save/load roundtrip", loaded.Count == 2 && loaded[1].Name == "Couch" && loaded[1].ScalePercentage == 200);
    Check("backup written on second save", File.Exists(PresetStore.BackupFilePath));

    // Legacy plain-array format still loads.
    File.WriteAllText(PresetStore.PresetFilePath, """[{"Name":"Legacy","ScalePercentage":150,"ApplyToAllMonitors":true,"DeviceNames":[]}]""");
    List<ScalingPreset> legacy = PresetStore.LoadOrCreateDefault();
    Check("legacy array format loads", legacy.Count == 1 && legacy[0].Name == "Legacy");

    // Corrupt primary + valid backup recovers. The backup holds the *previous* save,
    // so save twice: after the second save, "Recovered" is the backup content.
    PresetStore.Save([Valid("Recovered")]);
    PresetStore.Save([Valid("Second")]);
    File.WriteAllText(PresetStore.PresetFilePath, "{ not json");
    List<ScalingPreset> recovered = PresetStore.LoadOrCreateDefault();
    Check("corrupt file recovers from backup", recovered.Count == 1 && recovered[0].Name == "Recovered" && PresetStore.RecoveredFromBackup);
    Check("corrupt file left untouched", File.ReadAllText(PresetStore.PresetFilePath) == "{ not json");

    // Export / import.
    string exportPath = Path.Combine(tempDir, "export.json");
    PresetStore.ExportTo(exportPath, [Valid("Exported", 175)]);
    Check("export writes file", File.Exists(exportPath));
    List<ScalingPreset> imported = PresetStore.ImportFrom(exportPath);
    Check("import roundtrip", imported.Count == 1 && imported[0].Name == "Exported" && imported[0].ScalePercentage == 175);
    Throws<FileNotFoundException>("import of missing file fails", () => PresetStore.ImportFrom(Path.Combine(tempDir, "nope.json")));
}
finally
{
    PresetStore.PresetFilePathOverride = null;
    AppSettingsStore.SettingsFilePathOverride = null;
    try { Directory.Delete(tempDir, recursive: true); } catch { }
}

Console.WriteLine("AppSettingsStore");
string tempDir2 = Path.Combine(Path.GetTempPath(), "WQS-Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDir2);
AppSettingsStore.SettingsFilePathOverride = Path.Combine(tempDir2, "settings.json");
try
{
    AppSettings defaults = AppSettingsStore.Load();
    Check("defaults: tray on, hotkeys on, desk 125, couch 200",
        defaults.MinimizeToTray && defaults.HotkeysEnabled && defaults.DeskScalePercent == 125 && defaults.CouchScalePercent == 200);

    var custom = new AppSettings { MinimizeToTray = false, DeskScalePercent = 999, CouchPresetName = "Couch" };
    custom.AppProfiles["game.exe"] = "Couch";
    AppSettingsStore.Save(custom);
    AppSettings reloaded = AppSettingsStore.Load();
    Check("settings roundtrip + invalid scale normalized",
        !reloaded.MinimizeToTray && reloaded.DeskScalePercent == 125 && reloaded.CouchPresetName == "Couch" && reloaded.AppProfiles["game.exe"] == "Couch");

    File.WriteAllText(AppSettingsStore.SettingsFilePath, "{broken");
    Check("corrupt settings fall back to defaults", AppSettingsStore.Load().DeskScalePercent == 125);
}
finally
{
    AppSettingsStore.SettingsFilePathOverride = null;
    try { Directory.Delete(tempDir2, recursive: true); } catch { }
}

Console.WriteLine("CliArgs");
CliOptions none = CliArgs.Parse([]);
Check("no args: no action", !none.HasApplyAction && !none.StartMinimized && !none.Help);
CliOptions tray = CliArgs.Parse(["--tray"]);
Check("--tray", tray.StartMinimized);
CliOptions preset = CliArgs.Parse(["--preset", "Couch Night", "--exit"]);
Check("--preset + --exit", preset.PresetName == "Couch Night" && preset.ExitAfterApply && preset.HasApplyAction);
CliOptions scale = CliArgs.Parse(["--scale", "200", "--monitor", "1,2"]);
Check("--scale + --monitor", scale.ScalePercent == 200 && scale.MonitorIndexes.SequenceEqual([1, 2]));
CliOptions device = CliArgs.Parse(["--scale", "150", "--device", @"\\.\DISPLAY2", "--all"]);
Check("--scale + --device + --all", scale.ScalePercent == 200 && device.DeviceNames.Count == 1 && device.ApplyToAll);
CliOptions desk = CliArgs.Parse(["--desk"]);
CliOptions couch = CliArgs.Parse(["--couch"]);
Check("--desk / --couch", desk.Desk && couch.Couch);
CliOptions exp = CliArgs.Parse(["--export", "a.json", "--import", "b.json"]);
Check("--export / --import", exp.ExportPath == "a.json" && exp.ImportPath == "b.json");
Check("--help", CliArgs.Parse(["--help"]).Help);
Check("unknown args ignored", !CliArgs.Parse(["--future-flag", "x"]).HasApplyAction);

Console.WriteLine("TrayMenuBuilder");
List<string> empty = TrayMenuBuilder.BuildLabels([]);
Check("empty menu order",
    empty.SequenceEqual(["Open Dashboard", TrayMenuBuilder.SeparatorLabel, "No saved presets", TrayMenuBuilder.SeparatorLabel, "Exit App"]));
List<string> withPresets = TrayMenuBuilder.BuildLabels(["Desk", "Couch"]);
Check("preset menu order preserves preset order",
    withPresets.SequenceEqual(["Open Dashboard", TrayMenuBuilder.SeparatorLabel, "Desk", "Couch", TrayMenuBuilder.SeparatorLabel, "Exit App"]));
Check("menu always ends with Exit", withPresets[^1] == "Exit App" && withPresets[0] == "Open Dashboard");

Console.WriteLine();
Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
