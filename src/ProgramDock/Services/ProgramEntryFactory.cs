using System.Diagnostics;
using System.IO;
using ProgramDock.Models;

namespace ProgramDock.Services;

public static class ProgramEntryFactory
{
    public static ProgramEntry FromFile(string path, ProgramSource source = ProgramSource.Disk, string? displayName = null)
    {
        var info = new FileInfo(path);
        var name = string.IsNullOrWhiteSpace(displayName)
            ? GetDescription(path) ?? System.IO.Path.GetFileNameWithoutExtension(path)
            : displayName;
        return new ProgramEntry(
            path,
            name,
            source,
            info.Exists ? info.Length : 0,
            info.Exists ? info.LastWriteTime : null,
            GetDescription(path),
            path,
            isSystemComponent: false,
            isCommandLineTool: false);
    }

    public static ProgramEntry FromInstalled(
        string path,
        string displayName,
        string? description = null,
        string? iconPath = null,
        bool isSystemComponent = false)
    {
        var info = new FileInfo(path);
        return new ProgramEntry(
            path,
            displayName,
            ProgramSource.Installed,
            info.Exists ? info.Length : 0,
            info.Exists ? info.LastWriteTime : null,
            description,
            iconPath,
            isSystemComponent,
            ExecutableInspector.IsConsoleExecutable(path));
    }

    public static string? GetDescription(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var description = info.FileDescription?.Trim();
            if (!string.IsNullOrWhiteSpace(description) && !IsGenericDescription(description))
            {
                return description;
            }

            var product = info.ProductName?.Trim();
            if (!string.IsNullOrWhiteSpace(product) && !IsGenericDescription(product))
            {
                return product;
            }
        }
        catch
        {
            // A damaged or inaccessible executable should still be launchable.
        }

        return null;
    }

    private static bool IsGenericDescription(string value)
    {
        return value.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Application", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Windows Application", StringComparison.OrdinalIgnoreCase);
    }
}
