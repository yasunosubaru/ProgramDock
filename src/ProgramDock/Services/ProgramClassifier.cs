using System.IO;
using ProgramDock.Models;

namespace ProgramDock.Services;

public sealed record ClassificationResult(ProgramCategory Category, string Reason);

/// <summary>
/// Provides a conservative first-pass classification. The user can always
/// override a decision from a program tile's context menu.
/// </summary>
public static class ProgramClassifier
{
    private static readonly HashSet<string> DirectWindowsExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe",
        "notepad.exe",
        "calc.exe",
        "mspaint.exe",
        "write.exe",
        "wordpad.exe",
        "charmap.exe",
        "magnify.exe",
        "narrator.exe",
        "snippingtool.exe",
        "wmplayer.exe",
        "photos.exe",
        "movies.exe",
        "camera.exe"
    };

    private static readonly string[] StrongSystemNameFragments =
    [
        "windows defender",
        "microsoft defender",
        "windows security",
        "windows settings",
        "windows update",
        "microsoft update",
        "windows driver",
        "windows sdk",
        "visual c++",
        "vcredist",
        "redistributable",
        "microsoft .net",
        ".net framework",
        ".net host",
        ".net native",
        "native binder",
        "hostfxr",
        "hostpolicy",
        "dotnet",
        ".net runtime",
        "dotnet runtime",
        "java runtime",
        "node.js runtime",
        "runtime",
        "framework",
        "webview2 runtime",
        ".net serialization",
        "serialization assistant",
        "mscorlib",
        "clrjit",
        "native image",
        "ngen",
        "reference assemblies",
        "language pack",
        "system configuration",
        "system restore",
        "windows installer",
        "windows recovery",
        "command prompt",
        "windows powershell",
        "powershell",
        "cmd.exe"
    ];

    private static readonly string[] HelperNameFragments =
    [
        "uninstall",
        "unins",
        "setup",
        "installer",
        "updater",
        "crashpad",
        "crash reporter",
        "crash handler",
        "diagnostic",
        "troubleshooter",
        "repair tool",
        "driver package",
        "driverbooster",
        "driver booster",
        "dpinst",
        "dpinst",
        "autonts",
        "dbdownloader",
        "dbinstaller",
        "netadpinstall",
        "backup",
        "boost",
        "service host",
        "background task",
        "activation",
        "activator",
        "keygen",
        "patch",
        "crack",
        "kms",
        "激活",
        "破解",
        "注册机",
        "自动",
        "合成"
    ];

    public static ClassificationResult Classify(
        ProgramEntry entry,
        AppSettings settings,
        ShortcutEvidence? shortcutEvidence = null)
    {
        var path = NormalizePath(entry.Path);
        if (settings.CategoryOverrides.TryGetValue(path, out var manual)
            && manual != ProgramCategory.Unclassified)
        {
            return new ClassificationResult(manual, "手动分类");
        }

        // Keep compatibility with settings written by the first prototype.
        if (settings.SystemSupportPaths.Contains(path))
        {
            return new ClassificationResult(ProgramCategory.SystemSupport, "手动分类");
        }

        if (settings.DirectUsePaths.Contains(path))
        {
            return new ClassificationResult(ProgramCategory.DirectUse, "手动分类");
        }

        if (shortcutEvidence?.Matches(entry) == true)
        {
            return new ClassificationResult(ProgramCategory.DirectUse, "桌面/快捷方式");
        }

        if (settings.FavoritePaths.Contains(path)
            || settings.RecentPrograms.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return new ClassificationResult(ProgramCategory.DirectUse, "收藏/最近使用");
        }

        if (entry.IsSystemComponent)
        {
            return new ClassificationResult(ProgramCategory.SystemSupport, "注册表系统组件");
        }

        if (entry.IsCommandLineTool || LooksLikeCommandLineTool(entry))
        {
            return new ClassificationResult(ProgramCategory.SystemSupport, "命令行工具");
        }

        return ClassifyDefault(entry);
    }

    public static ClassificationResult ClassifyDefault(ProgramEntry entry)
    {
        var path = NormalizePath(entry.Path);
        var fileName = GetFileName(path);

        // Store apps in WindowsApps are often direct-use applications. Only
        // classify them as system/support when metadata looks like a runtime,
        // driver, update, or Windows maintenance component.
        if (ContainsPathFragment(path, "\\windowsapps\\"))
        {
            return entry.Source == ProgramSource.Installed && !LooksSystem(entry)
                ? new ClassificationResult(ProgramCategory.Unclassified, "Windows 应用（未有快捷方式证据）")
                : new ClassificationResult(ProgramCategory.SystemSupport, "WindowsApps 系统组件");
        }

        if (IsUtilityExtension(path))
        {
            return new ClassificationResult(ProgramCategory.SystemSupport, "脚本/安装包");
        }

        if (IsUnderWindowsOrSystemDirectory(path) || IsSystemDirectory(path))
        {
            return DirectWindowsExecutables.Contains(fileName)
                ? new ClassificationResult(ProgramCategory.DirectUse, "Windows 常用工具")
                : new ClassificationResult(ProgramCategory.SystemSupport, "Windows 核心路径");
        }

        if (string.Equals(Path.GetExtension(path), ".msi", StringComparison.OrdinalIgnoreCase))
        {
            return new ClassificationResult(ProgramCategory.SystemSupport, "安装包");
        }

        if (LooksSystem(entry))
        {
            return new ClassificationResult(ProgramCategory.SystemSupport, "系统/辅助关键词");
        }

        // Without a shortcut, favorite, recent, or manual signal, keep the
        // entry out of the small DirectUse view. The System/Support view
        // includes this conservative "unconfirmed" bucket.
        return new ClassificationResult(ProgramCategory.Unclassified, "未发现直接使用证据");
    }

    private static bool IsUnderWindowsOrSystemDirectory(string path)
    {
        var windowsRoot = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        return !string.IsNullOrWhiteSpace(windowsRoot) && IsUnder(path, windowsRoot);
    }

    private static bool IsSystemDirectory(string path)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86)
        };

        if (roots.Any(root => !string.IsNullOrWhiteSpace(root) && IsUnder(path, NormalizePath(root))))
        {
            return true;
        }

        string[] markers =
        [
            "\\program files\\windowsapps\\",
            "\\program files (x86)\\windowsapps\\",
            "\\programdata\\microsoft\\windows\\",
            "\\microsoft.net\\",
            "\\reference assemblies\\",
            "\\dotnet\\",
            "\\program files\\common files\\microsoft shared\\",
            "\\program files (x86)\\common files\\microsoft shared\\",
            "\\appdata\\local\\microsoft\\windowsapps\\",
            "\\windows\\installer\\",
            "\\windows\\system32\\",
            "\\windows\\syswow64\\",
            "\\windows\\driverstore\\",
            "\\windows\\winsxs\\",
            "\\windows\\assembly\\",
            "\\windows\\microsoft.net\\",
            "\\windows\\systemapps\\",
            "\\windows\\softwaredistribution\\",
            "\\windows\\temp\\",
            "\\system volume information\\"
        ];
        return markers.Any(marker => path.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksSystem(ProgramEntry entry)
    {
        var name = (entry.Name ?? string.Empty).ToLowerInvariant();
        var fileName = GetFileNameWithoutExtension(entry.Path);
        var description = (entry.Description ?? string.Empty).ToLowerInvariant();
        var text = $"{name} {fileName} {description}";

        if (StrongSystemNameFragments.Any(fragment => text.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Generic helper names are only used for files/shortcuts. This avoids
        // hiding a legitimate installed product merely because its name has a
        // word such as "update" in a marketing title.
        if (entry.Source != ProgramSource.Installed
            && HelperNameFragments.Any(fragment => text.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeCommandLineTool(ProgramEntry entry)
    {
        var fileName = GetFileNameWithoutExtension(entry.Path);
        var nameFragments = new[]
        {
            "lldb", "glsl", "spirv", "afm", "copyinto", "run_", "bench",
            "compile", "codegen", "cmake", "ninja", "objdump", "readelf",
            "objcopy", "strip"
        };
        if (fileName.StartsWith('_') || fileName.StartsWith("._")
            || nameFragments.Any(fragment => fileName.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (entry.Source != ProgramSource.Installed
            && new[] { "unins", "setup", "update", "helper" }
                .Any(fragment => fileName.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        string[] toolDirectories =
        [
            "\\texlive\\", "\\miktex\\", "\\cargo\\bin\\", "\\android\\ndk\\",
            "\\llvm\\", "\\mingw\\", "\\msys\\", "\\node_modules\\",
            "\\source\\", "\\src\\", "\\build\\", "\\obj\\", "\\target\\",
            "\\vendor\\", "\\sdk\\"
        ];
        return toolDirectories.Any(fragment => entry.Path.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUtilityExtension(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".bat" or ".cmd" or ".ps1" or ".vbs" or ".js"
            or ".msi" or ".msix" or ".appx" or ".sys" or ".inf" or ".cat";
    }

    private static bool IsUnder(string path, string root)
    {
        var normalizedRoot = root.TrimEnd('\\');
        return path.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normalizedRoot + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsPathFragment(string path, string fragment)
    {
        var padded = path.StartsWith('\\') ? path : "\\" + path;
        return padded.Contains(fragment, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path).Replace('/', '\\');
        }
        catch
        {
            return path.Replace('/', '\\');
        }
    }

    private static string GetFileName(string path)
    {
        try
        {
            return Path.GetFileName(path).ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetFileNameWithoutExtension(string path)
    {
        try
        {
            return Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }
}
