using System.Diagnostics;
using System.IO;
using ProgramDock.Models;

namespace ProgramDock.Services;

public static class LaunchService
{
    public static bool TryLaunch(ProgramEntry entry, out string? error)
    {
        error = null;
        try
        {
            var path = Path.GetFullPath(entry.Path);
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                error = "文件已不存在，可能已被移动或卸载。";
                return false;
            }

            var extension = Path.GetExtension(path).ToLowerInvariant();
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = extension is ".lnk" or ".url" or ".msi" or ".msix" or ".appx",
                WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory
            };

            if (extension is ".bat" or ".cmd")
            {
                // ShellExecute is intentionally avoided for scripts. ArgumentList
                // prevents a path containing spaces or quotes from becoming a command.
                startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                startInfo.UseShellExecute = false;
                startInfo.ArgumentList.Add("/d");
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add(path);
            }

            Process.Start(startInfo);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public static void Reveal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + path + "\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // Explorer may be unavailable in a restricted session.
        }
    }

    public static void OpenFolder(string path)
    {
        try
        {
            var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore shell integration failures.
        }
    }

    public static void ShowProperties(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + path + "\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore shell integration failures.
        }
    }
}
