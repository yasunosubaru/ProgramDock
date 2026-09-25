using System.IO;
using Microsoft.Win32;
using ProgramDock.Models;

namespace ProgramDock.Services;

public sealed class RegistryAppProvider
{
    private static readonly string[] RegistryPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    public IReadOnlyList<ProgramEntry> Discover()
    {
        var results = new List<ProgramEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var registryPath in RegistryPaths)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                    using var key = baseKey.OpenSubKey(registryPath);
                    if (key is null)
                    {
                        continue;
                    }

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var appKey = key.OpenSubKey(subKeyName);
                            var displayName = appKey?.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(displayName))
                            {
                                continue;
                            }

                            var iconValue = appKey?.GetValue("DisplayIcon") as string;
                            var installLocation = appKey?.GetValue("InstallLocation") as string;
                            var isSystemComponent = ReadBoolean(appKey, "SystemComponent");
                            var target = ResolveTarget(iconValue, installLocation);
                            if (target is null || !File.Exists(target))
                            {
                                continue;
                            }

                            if (!seen.Add(Path.GetFullPath(target)))
                            {
                                continue;
                            }

                            results.Add(ProgramEntryFactory.FromInstalled(
                                target,
                                displayName.Trim(),
                                ProgramEntryFactory.GetDescription(target),
                                target,
                                isSystemComponent));
                        }
                        catch
                        {
                            // Ignore malformed uninstall entries.
                        }
                    }
                }
                catch
                {
                    // A missing or inaccessible registry view is not fatal.
                }
            }
        }

        return results
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string? ResolveTarget(string? iconValue, string? installLocation)
    {
        if (!string.IsNullOrWhiteSpace(iconValue))
        {
            var candidate = iconValue.Trim().Trim('"');
            var commaIndex = candidate.LastIndexOf(',');
            if (commaIndex > 1 && int.TryParse(candidate[(commaIndex + 1)..], out _))
            {
                candidate = candidate[..commaIndex].Trim().Trim('"');
            }

            if (File.Exists(candidate) && IsUsableLaunchTarget(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        if (string.IsNullOrWhiteSpace(installLocation))
        {
            return null;
        }

        try
        {
            if (!Directory.Exists(installLocation))
            {
                return null;
            }

            var candidates = Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(File.Exists)
                .ToList();
            return candidates.FirstOrDefault(IsLikelyUserTarget)
                ?? candidates.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool ReadBoolean(RegistryKey? key, string name)
    {
        try
        {
            return Convert.ToInt32(key?.GetValue(name, 0) ?? 0) == 1;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsUsableLaunchTarget(string path)
    {
        var fileName = Path.GetFileName(path);
        return !string.IsNullOrWhiteSpace(fileName)
            && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyUserTarget(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return !name.Contains("unins", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("setup", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("update", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("helper", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("crash", StringComparison.OrdinalIgnoreCase);
    }
}
