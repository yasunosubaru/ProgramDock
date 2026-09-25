using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ProgramDock.Models;

public enum ProgramSource
{
    Disk,
    Installed,
    Shortcut
}

public enum ProgramCategory
{
    Unclassified = 0,
    DirectUse = 1,
    SystemSupport = 2
}

public sealed class ProgramEntry : INotifyPropertyChanged
{
    private string _name;
    private bool _isFavorite;
    private bool _isSelected;
    private ImageSource? _icon;
    private DateTime? _lastLaunchedAt;
    private ProgramCategory _autoCategory = ProgramCategory.Unclassified;
    private ProgramCategory _category = ProgramCategory.Unclassified;
    private string _categoryReason = string.Empty;
    private bool _hasManualCategoryOverride;
    private bool _isSystemComponent;
    private bool _isCommandLineTool;

    public ProgramEntry(
        string path,
        string name,
        ProgramSource source,
        long size = 0,
        DateTime? modifiedAt = null,
        string? description = null,
        string? iconPath = null,
        bool isSystemComponent = false,
        bool isCommandLineTool = false)
    {
        Path = NormalizePath(path);
        _name = string.IsNullOrWhiteSpace(name) ? GetFallbackName(Path) : name.Trim();
        Source = source;
        Size = size;
        ModifiedAt = modifiedAt;
        Description = description ?? string.Empty;
        IconPath = string.IsNullOrWhiteSpace(iconPath) ? Path : iconPath;
        IsSystemComponent = isSystemComponent;
        IsCommandLineTool = isCommandLineTool;
    }

    public string Path { get; }

    public string Name
    {
        get => _name;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? GetFallbackName(Path) : value.Trim();
            if (string.Equals(_name, next, StringComparison.Ordinal))
            {
                return;
            }

            _name = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SearchText));
        }
    }

    public ProgramSource Source { get; }

    public string SourceLabel => Source switch
    {
        ProgramSource.Installed => "已安装",
        ProgramSource.Shortcut => "快捷方式",
        _ => "磁盘文件"
    };

    public ProgramCategory AutoCategory
    {
        get => _autoCategory;
        set
        {
            if (_autoCategory == value)
            {
                return;
            }

            _autoCategory = value;
            OnPropertyChanged();
        }
    }

    public ProgramCategory Category
    {
        get => _category;
        set
        {
            if (_category == value)
            {
                return;
            }

            _category = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CategoryLabel));
            OnPropertyChanged(nameof(SearchText));
        }
    }

    public string CategoryReason
    {
        get => _categoryReason;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_categoryReason, next, StringComparison.Ordinal))
            {
                return;
            }

            _categoryReason = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SearchText));
        }
    }

    public bool HasManualCategoryOverride
    {
        get => _hasManualCategoryOverride;
        set
        {
            if (_hasManualCategoryOverride == value)
            {
                return;
            }

            _hasManualCategoryOverride = value;
            OnPropertyChanged();
        }
    }

    public string CategoryLabel => Category switch
    {
        ProgramCategory.SystemSupport => "系统/辅助",
        ProgramCategory.DirectUse => "直接使用",
        _ => "系统/辅助"
    };

    public long Size { get; }

    public DateTime? ModifiedAt { get; }

    public string Description { get; set; }

    public string IconPath { get; }

    public bool IsSystemComponent
    {
        get => _isSystemComponent;
        set
        {
            if (_isSystemComponent == value)
            {
                return;
            }

            _isSystemComponent = value;
            OnPropertyChanged();
        }
    }

    public bool IsCommandLineTool
    {
        get => _isCommandLineTool;
        set
        {
            if (_isCommandLineTool == value)
            {
                return;
            }

            _isCommandLineTool = value;
            OnPropertyChanged();
        }
    }

    public string Directory
    {
        get
        {
            try
            {
                return System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public string SearchText => string.Join(
        " ",
        Name,
        Path,
        Description,
        SourceLabel,
        CategoryLabel,
        CategoryReason,
        System.IO.Path.GetFileNameWithoutExtension(Path));

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
            {
                return;
            }

            _isFavorite = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value))
            {
                return;
            }

            _icon = value;
            OnPropertyChanged();
        }
    }

    public DateTime? LastLaunchedAt
    {
        get => _lastLaunchedAt;
        set
        {
            if (_lastLaunchedAt == value)
            {
                return;
            }

            _lastLaunchedAt = value;
            OnPropertyChanged();
        }
    }

    public string SizeDisplay
    {
        get
        {
            if (Size <= 0)
            {
                return string.Empty;
            }

            string[] units = ["B", "KB", "MB", "GB", "TB"];
            var value = (double)Size;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return $"{value:0.#} {units[unit]}";
        }
    }

    public void ApplyClassification(
        ProgramCategory autoCategory,
        string reason,
        ProgramCategory effectiveCategory,
        bool hasManualOverride)
    {
        AutoCategory = autoCategory;
        CategoryReason = reason;
        Category = effectiveCategory;
        HasManualCategoryOverride = hasManualOverride;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        NotifyPropertyChanged(propertyName);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static string GetFallbackName(string path)
    {
        try
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrWhiteSpace(name) ? path : name;
        }
        catch
        {
            return path;
        }
    }
}
