using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ProgramDock.Models;

namespace ProgramDock.Services;

public sealed record ShortcutTargetInfo(string TargetPath, string Arguments, string WorkingDirectory);

public sealed class ShortcutEvidence
{
    public HashSet<string> TargetPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ShortcutPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ShortcutNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, ShortcutTargetInfo> ShortcutTargets { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int Count => TargetPaths.Count + ShortcutPaths.Count;

    public bool Matches(ProgramEntry entry)
    {
        return TargetPaths.Contains(entry.Path) || ShortcutPaths.Contains(entry.Path);
    }

    public string GetGroupKey(ProgramEntry entry)
    {
        if (entry.Source == ProgramSource.Shortcut
            && ShortcutTargets.TryGetValue(entry.Path, out var target))
        {
            return string.Join("\u001f", target.TargetPath, target.Arguments, target.WorkingDirectory);
        }

        return "shortcut:" + entry.Path;
    }
}

public static class ShortcutEvidenceProvider
{
    public static ShortcutEvidence Discover(IEnumerable<string>? additionalRoots = null)
    {
        var evidence = new ShortcutEvidence();
        var roots = new HashSet<string>(GetDefaultRoots(), StringComparer.OrdinalIgnoreCase);
        if (additionalRoots is not null)
        {
            foreach (var root in additionalRoots.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                roots.Add(root);
            }
        }

        Type? shellType = null;
        object? shell = null;
        try
        {
            shellType = Type.GetTypeFromProgID("WScript.Shell");
            shell = shellType is null ? null : Activator.CreateInstance(shellType);
        }
        catch
        {
            // Shortcut files themselves are still useful evidence if COM is unavailable.
        }

        try
        {
            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                IEnumerable<string> shortcutFiles;
                try
                {
                    shortcutFiles = Directory.EnumerateFiles(root, "*.lnk", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                foreach (var shortcutPath in shortcutFiles)
                {
                    AddPath(evidence.ShortcutPaths, shortcutPath);
                    AddName(evidence.ShortcutNames, Path.GetFileNameWithoutExtension(shortcutPath));
                    if (shellType is not null && shell is not null)
                    {
                        var target = ResolveTarget(shellType, shell, shortcutPath);
                        if (target is not null)
                        {
                            AddPath(evidence.TargetPaths, target.TargetPath);
                            var normalizedShortcutPath = NormalizePath(shortcutPath);
                            evidence.ShortcutTargets[normalizedShortcutPath] = target;
                        }
                    }
                }
            }
        }
        finally
        {
            if (shell is not null)
            {
                try
                {
                    Marshal.FinalReleaseComObject(shell);
                }
                catch
                {
                    // Ignore COM cleanup failures.
                }
            }
        }

        return evidence;
    }

    private static IEnumerable<string> GetDefaultRoots()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(documents))
        {
            yield return Path.Combine(documents, "快捷方式");
        }

        if (!string.IsNullOrWhiteSpace(desktop))
        {
            yield return desktop;
        }
    }

    private static ShortcutTargetInfo? ResolveTarget(Type shellType, object shell, string shortcutPath)
    {
        object? shortcut = null;
        try
        {
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                shell,
                [shortcutPath]);
            if (shortcut is null)
            {
                return null;
            }

            var target = shortcut.GetType().InvokeMember(
                "TargetPath",
                BindingFlags.GetProperty,
                binder: null,
                shortcut,
                null) as string;
            if (string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            target = target.Trim().Trim('"');
            if (!File.Exists(target) && !Directory.Exists(target))
            {
                return null;
            }

            var arguments = shortcut.GetType().InvokeMember(
                "Arguments",
                BindingFlags.GetProperty,
                binder: null,
                shortcut,
                null) as string ?? string.Empty;
            var workingDirectory = shortcut.GetType().InvokeMember(
                "WorkingDirectory",
                BindingFlags.GetProperty,
                binder: null,
                shortcut,
                null) as string ?? string.Empty;
            return new ShortcutTargetInfo(
                Path.GetFullPath(target),
                arguments.Trim(),
                workingDirectory.Trim());
        }
        catch
        {
            return null;
        }
        finally
        {
            if (shortcut is not null)
            {
                try
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }
                catch
                {
                    // Ignore COM cleanup failures.
                }
            }
        }
    }

    private static string NormalizePath(string? path)
    {
        try
        {
            return Path.GetFullPath(path ?? string.Empty);
        }
        catch
        {
            return path ?? string.Empty;
        }
    }

    private static void AddName(HashSet<string> names, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            names.Add(name);
        }
    }

    private static void AddPath(HashSet<string> paths, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            paths.Add(Path.GetFullPath(path));
        }
        catch
        {
            paths.Add(path);
        }
    }
}
