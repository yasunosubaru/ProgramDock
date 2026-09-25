using System.IO;
using ProgramDock.Models;

namespace ProgramDock.Services;

public sealed class ScanProgress
{
    public int FilesSeen { get; init; }
    public int Matches { get; init; }
    public int Errors { get; init; }
    public string CurrentPath { get; init; } = string.Empty;
    public IReadOnlyList<ProgramEntry> Batch { get; init; } = [];
}

public sealed class ProgramScanner
{
    private static readonly string[] AlwaysExcludedDirectoryNames =
    [
        "$recycle.bin",
        "system volume information",
        "windows.old",
        "recovery"
    ];

    private static readonly string[] ExcludedPathFragments =
    [
        "\\windows\\winsxs\\",
        "\\windows\\servicing\\",
        "\\windows\\software distribution\\download\\",
        "\\$recycle.bin\\",
        "\\system volume information\\"
    ];

    public Task<IReadOnlyList<ProgramEntry>> ScanAsync(
        IReadOnlyList<string> roots,
        IReadOnlyCollection<string> extensions,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<ProgramEntry>>(() =>
        {
            var allowedExtensions = new HashSet<string>(
                extensions.Select(value => value.StartsWith('.') ? value : "." + value),
                StringComparer.OrdinalIgnoreCase);
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<ProgramEntry>();
            var batch = new List<ProgramEntry>();
            var filesSeen = 0;
            var errors = 0;
            var lastReport = DateTime.MinValue;

            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var pending = new Stack<DirectoryInfo>();
                var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                pending.Push(new DirectoryInfo(root));

                while (pending.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var directory = pending.Pop();
                    string directoryPath;
                    try
                    {
                        directoryPath = Path.GetFullPath(directory.FullName);
                    }
                    catch
                    {
                        errors++;
                        continue;
                    }

                    if (!visitedDirectories.Add(directoryPath))
                    {
                        continue;
                    }

                    if (IsExcluded(directoryPath))
                    {
                        continue;
                    }

                    try
                    {
                        foreach (var file in directory.EnumerateFiles("*", new EnumerationOptions
                        {
                            RecurseSubdirectories = false,
                            IgnoreInaccessible = true,
                            ReturnSpecialDirectories = false,
                            AttributesToSkip = FileAttributes.ReparsePoint
                        }))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            filesSeen++;
                            if (!allowedExtensions.Contains(file.Extension))
                            {
                                continue;
                            }

                            string fullPath;
                            try
                            {
                                fullPath = Path.GetFullPath(file.FullName);
                                if (IsExcluded(fullPath) || !seenPaths.Add(fullPath))
                                {
                                    continue;
                                }

                                batch.Add(ProgramEntryFactory.FromFile(
                                    fullPath,
                                    file.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                                        ? ProgramSource.Shortcut
                                        : ProgramSource.Disk));
                            }
                            catch
                            {
                                errors++;
                            }

                            if (batch.Count >= 24)
                            {
                                results.AddRange(batch);
                                Report(progress, filesSeen, results.Count, errors, file.FullName, batch, ref lastReport);
                                batch.Clear();
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        errors++;
                    }

                    try
                    {
                        foreach (var child in directory.EnumerateDirectories("*", new EnumerationOptions
                        {
                            RecurseSubdirectories = false,
                            IgnoreInaccessible = true,
                            ReturnSpecialDirectories = false,
                            AttributesToSkip = FileAttributes.ReparsePoint
                        }))
                        {
                            if (!IsExcluded(child.FullName))
                            {
                                pending.Push(child);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        errors++;
                    }

                    if ((DateTime.UtcNow - lastReport).TotalMilliseconds >= 120)
                    {
                        Report(progress, filesSeen, results.Count, errors, directoryPath, batch, ref lastReport);
                        batch.Clear();
                    }
                }
            }

            if (batch.Count > 0)
            {
                results.AddRange(batch);
            }

            Report(progress, filesSeen, results.Count, errors, "扫描完成", [], ref lastReport, force: true);
            return results;
        }, cancellationToken);
    }

    private static void Report(
        IProgress<ScanProgress>? progress,
        int filesSeen,
        int matches,
        int errors,
        string currentPath,
        IReadOnlyList<ProgramEntry> batch,
        ref DateTime lastReport,
        bool force = false)
    {
        if (!force && (DateTime.UtcNow - lastReport).TotalMilliseconds < 100)
        {
            return;
        }

        lastReport = DateTime.UtcNow;
        progress?.Report(new ScanProgress
        {
            FilesSeen = filesSeen,
            Matches = matches,
            Errors = errors,
            CurrentPath = currentPath,
            Batch = batch.ToArray()
        });
    }

    private static bool IsExcluded(string path)
    {
        var normalized = path.Replace('/', '\\');
        if (ExcludedPathFragments.Any(fragment => normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return AlwaysExcludedDirectoryNames.Any(name =>
            normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment.Equals(name, StringComparison.OrdinalIgnoreCase)));
    }
}
