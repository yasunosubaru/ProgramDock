using System.IO;

namespace ProgramDock.Services;

/// <summary>
/// Centralizes the public product name and per-user data locations.
/// </summary>
public static class AppPaths
{
    public const string ProductName = "ProgramDock";
    public const string AppDataFolderName = ProductName;
    public const string LegacyAppDataFolderName = "QLaunchWin";

    public static string AppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppDataFolderName);

    public static string SettingsPath => Path.Combine(AppDataDirectory, "settings.json");
    public static string CatalogPath => Path.Combine(AppDataDirectory, "catalog.json");
    public static string CrashLogPath => Path.Combine(AppDataDirectory, "crash.log");

    /// <summary>
    /// Copies the previous local profile once when upgrading an existing install.
    /// The legacy folder name is intentionally kept only for this migration path.
    /// </summary>
    public static void MigrateLegacyData()
    {
        try
        {
            var legacyDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                LegacyAppDataFolderName);
            if (!Directory.Exists(legacyDirectory)
                || string.Equals(
                    legacyDirectory,
                    AppDataDirectory,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Directory.CreateDirectory(AppDataDirectory);
            foreach (var fileName in new[] { "settings.json", "catalog.json" })
            {
                var legacyFile = Path.Combine(legacyDirectory, fileName);
                var currentFile = Path.Combine(AppDataDirectory, fileName);
                if (File.Exists(legacyFile) && !File.Exists(currentFile))
                {
                    File.Copy(legacyFile, currentFile, overwrite: false);
                }
            }
        }
        catch
        {
            // Migration is best effort and must never prevent the launcher from starting.
        }
    }
}
