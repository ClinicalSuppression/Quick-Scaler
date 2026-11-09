using System.IO;
using Microsoft.Win32;

namespace QuickScaler
{
    /// <summary>
    /// One-time migration of state written by the pre-rename builds
    /// ("Windows Quick Scaler" / "WindowsQuickScaler") to the Quick Scaler names.
    /// Safe to call on every startup; it no-ops when there is nothing to migrate.
    /// </summary>
    internal static class AppPaths
    {
        internal const string AppFolderName = "QuickScaler";
        internal const string LegacyFolderName = "WindowsQuickScaler";
        internal const string StartupValueName = "QuickScaler";
        internal const string LegacyStartupValueName = "WindowsQuickScaler";

        internal static string DataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName);

        /// <summary>
        /// Migrate %LOCALAPPDATA%\WindowsQuickScaler to %LOCALAPPDATA%\QuickScaler.
        /// If only the legacy folder exists it is moved; if both exist, any files missing
        /// from the current folder are moved out of the legacy one (never overwritten).
        /// </summary>
        internal static void MigrateLegacyDataDirectory()
        {
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string legacy = Path.Combine(local, LegacyFolderName);
                string current = Path.Combine(local, AppFolderName);
                if (Directory.Exists(legacy) && !Directory.Exists(current))
                {
                    Directory.Move(legacy, current);
                    return;
                }
                if (!Directory.Exists(legacy))
                    return;

                Directory.CreateDirectory(current);
                foreach (string legacyFile in Directory.GetFiles(legacy, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(legacy, legacyFile);
                    string destination = Path.Combine(current, relative);
                    if (File.Exists(destination))
                        continue; // never clobber current data
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(legacyFile, destination);
                }
                // Remove the legacy folder only if it is empty now.
                try
                {
                    if (!Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories).Any()
                        && !Directory.EnumerateDirectories(legacy, "*", SearchOption.AllDirectories).Any())
                        Directory.Delete(legacy, recursive: true);
                }
                catch
                {
                    // Non-fatal: leftover legacy folder can stay.
                }
            }
            catch (Exception exception)
            {
                App.LogError(exception);
            }
        }

        /// <summary>Rewrite the legacy Run key value (old exe path) to point at this executable, then drop the legacy value.</summary>
        internal static void MigrateLegacyStartupEntry()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (key?.GetValue(LegacyStartupValueName) is null)
                    return;
                string? executablePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(executablePath))
                    key.SetValue(StartupValueName, $"\"{executablePath}\" --tray");
                key.DeleteValue(LegacyStartupValueName, throwOnMissingValue: false);
            }
            catch (Exception exception)
            {
                App.LogError(exception);
            }
        }
    }
}
