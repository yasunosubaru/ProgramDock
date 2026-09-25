using System.Text.Json.Serialization;

namespace ProgramDock.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 2;

    public List<string> ScanRoots { get; set; } = [];

    public List<string> Extensions { get; set; } =
    [
        ".exe", ".com", ".bat", ".cmd", ".lnk", ".msi", ".msix", ".appx"
    ];

    public HashSet<string> FavoritePaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> HiddenPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>用户手动指定为“直接使用”的路径。</summary>
    public HashSet<string> DirectUsePaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>用户手动指定为“系统/辅助”的路径。</summary>
    public HashSet<string> SystemSupportPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>新版分类覆盖表；旧版两组路径会自动迁移到这里。</summary>
    public Dictionary<string, ProgramCategory> CategoryOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<RecentProgram> RecentPrograms { get; set; } = [];

    public bool ScanOnStartup { get; set; } = true;

    public bool CloseToTray { get; set; } = true;

    public bool HideAfterLaunch { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public bool ShowHiddenPrograms { get; set; }

    public int PageSize { get; set; } = 40;

    public string GlobalHotkey { get; set; } = "Ctrl+Alt+Space";

    public DateTime? LastScanAt { get; set; }

    public void Normalize()
    {
        ScanRoots ??= [];
        Extensions ??= [];
        FavoritePaths = new HashSet<string>(FavoritePaths ?? [], StringComparer.OrdinalIgnoreCase);
        HiddenPaths = new HashSet<string>(HiddenPaths ?? [], StringComparer.OrdinalIgnoreCase);
        DirectUsePaths = new HashSet<string>(DirectUsePaths ?? [], StringComparer.OrdinalIgnoreCase);
        SystemSupportPaths = new HashSet<string>(SystemSupportPaths ?? [], StringComparer.OrdinalIgnoreCase);
        var normalizedOverrides = new Dictionary<string, ProgramCategory>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in DirectUsePaths)
        {
            normalizedOverrides[path] = ProgramCategory.DirectUse;
        }

        foreach (var path in SystemSupportPaths)
        {
            normalizedOverrides[path] = ProgramCategory.SystemSupport;
        }

        foreach (var pair in CategoryOverrides ?? [])
        {
            if (!string.IsNullOrWhiteSpace(pair.Key)
                && Enum.IsDefined(typeof(ProgramCategory), pair.Value)
                && pair.Value != ProgramCategory.Unclassified)
            {
                normalizedOverrides[pair.Key] = pair.Value;
            }
        }

        CategoryOverrides = normalizedOverrides;
        RecentPrograms ??= [];

        var normalizedExtensions = new List<string>();
        foreach (var extension in Extensions)
        {
            var value = (extension ?? string.Empty).Trim().ToLowerInvariant();
            if (value.Length == 0)
            {
                continue;
            }

            if (!value.StartsWith('.'))
            {
                value = "." + value;
            }

            if (value.Length > 1 && normalizedExtensions.Contains(value, StringComparer.OrdinalIgnoreCase) is false)
            {
                normalizedExtensions.Add(value);
            }
        }

        Extensions = normalizedExtensions.Count > 0 ? normalizedExtensions :
        [
            ".exe", ".com", ".bat", ".cmd", ".lnk", ".msi", ".msix", ".appx"
        ];

        // Migrate the old five-column default to the compact high-density grid.
        if (PageSize == 35)
        {
            PageSize = 40;
        }

        PageSize = Math.Clamp(PageSize, 12, 96);
        GlobalHotkey ??= string.Empty;
        RecentPrograms = RecentPrograms
            .Where(item => !string.IsNullOrWhiteSpace(item?.Path))
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.LastLaunchedAt).First())
            .Take(30)
            .ToList();
    }

    public void SetCategoryOverride(string path, ProgramCategory category)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        DirectUsePaths.Remove(path);
        SystemSupportPaths.Remove(path);
        CategoryOverrides ??= new Dictionary<string, ProgramCategory>(StringComparer.OrdinalIgnoreCase);
        if (category == ProgramCategory.Unclassified)
        {
            CategoryOverrides.Remove(path);
            return;
        }

        CategoryOverrides[path] = category;
    }

    public void ClearCategoryOverride(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        DirectUsePaths.Remove(path);
        SystemSupportPaths.Remove(path);
        CategoryOverrides?.Remove(path);
    }

    public void AddRecent(ProgramEntry entry)
    {
        if (entry is null)
        {
            return;
        }

        RecentPrograms.RemoveAll(item => string.Equals(item.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
        RecentPrograms.Insert(0, new RecentProgram
        {
            Path = entry.Path,
            Name = entry.Name,
            LastLaunchedAt = DateTime.Now
        });
        if (RecentPrograms.Count > 30)
        {
            RecentPrograms = RecentPrograms.Take(30).ToList();
        }
    }
}

public sealed class RecentProgram
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime LastLaunchedAt { get; set; } = DateTime.Now;
}
