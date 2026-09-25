using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ProgramDock.Models;
using ProgramDock.Services;
using Forms = System.Windows.Forms;

namespace ProgramDock;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _settingsStore;
    private readonly ObservableCollection<DriveCheckItem> _driveItems = [];
    private bool _loading;

    public SettingsWindow(AppSettings settings, SettingsStore settingsStore)
    {
        InitializeComponent();
        _settingsStore = settingsStore;
        Settings = Clone(settings);
        DriveItemsControl.ItemsSource = _driveItems;
    }

    public AppSettings Settings { get; private set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        try
        {
            PopulateDriveItems();
            ScanOnStartupCheck.IsChecked = Settings.ScanOnStartup;
            CloseToTrayCheck.IsChecked = Settings.CloseToTray;
            HideAfterLaunchCheck.IsChecked = Settings.HideAfterLaunch;
            StartWithWindowsCheck.IsChecked = Settings.StartWithWindows;
            ShowHiddenCheck.IsChecked = Settings.ShowHiddenPrograms;
            ExtensionsTextBox.Text = string.Join(", ", Settings.Extensions);
            HotkeyTextBox.Text = Settings.GlobalHotkey;
            SelectPageSize(Settings.PageSize);
        }
        finally
        {
            _loading = false;
        }
    }

    private void PopulateDriveItems()
    {
        var configured = new HashSet<string>(Settings.ScanRoots, StringComparer.OrdinalIgnoreCase);
        var allPaths = new List<(string Path, string Label)>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if ((drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable) && drive.IsReady)
                {
                    allPaths.Add((drive.RootDirectory.FullName, drive.VolumeLabel is { Length: > 0 } label
                        ? $"{drive.Name}  {label}"
                        : drive.Name));
                }
            }
        }
        catch
        {
            // The settings page can still save custom paths if drive enumeration fails.
        }

        foreach (var configuredPath in Settings.ScanRoots.Where(path => !allPaths.Any(item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase))))
        {
            allPaths.Add((configuredPath, "自定义 · " + configuredPath));
        }

        foreach (var item in allPaths)
        {
            _driveItems.Add(new DriveCheckItem
            {
                Path = item.Path,
                DisplayName = item.Label,
                IsChecked = configured.Count == 0 || configured.Contains(item.Path)
            });
        }

        if (_driveItems.Count == 0)
        {
            _driveItems.Add(new DriveCheckItem
            {
                Path = "C:\\",
                DisplayName = "C:\\",
                IsChecked = true
            });
        }
    }

    private void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择要扫描的文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        var path = Path.GetFullPath(dialog.SelectedPath);
        if (_driveItems.Any(item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _driveItems.Add(new DriveCheckItem
        {
            Path = path,
            DisplayName = "自定义 · " + path,
            IsChecked = true
        });
    }

    private void OnCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var selectedRoots = _driveItems
            .Where(item => item.IsChecked)
            .Select(item => item.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selectedRoots.Count == 0)
        {
            ShowMessage("至少选择一个扫描范围。", true);
            return;
        }

        var extensions = ParseExtensions(ExtensionsTextBox.Text);
        if (extensions.Count == 0)
        {
            ShowMessage("请至少输入一个文件扩展名，例如 .exe。", true);
            return;
        }

        var hotkey = HotkeyTextBox.Text.Trim();
        if (hotkey.Length > 0 && !HotKeyService.TryParse(hotkey, out _, out _))
        {
            ShowMessage("快捷键格式无效。示例：Ctrl+Alt+Space。", true);
            return;
        }

        Settings.ScanRoots = selectedRoots;
        Settings.Extensions = extensions;
        Settings.ScanOnStartup = ScanOnStartupCheck.IsChecked == true;
        Settings.CloseToTray = CloseToTrayCheck.IsChecked == true;
        Settings.HideAfterLaunch = HideAfterLaunchCheck.IsChecked == true;
        Settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        Settings.ShowHiddenPrograms = ShowHiddenCheck.IsChecked == true;
        Settings.GlobalHotkey = hotkey;
        Settings.PageSize = GetSelectedPageSize();
        Settings.Normalize();

        try
        {
            StartupService.SetEnabled(Settings.StartWithWindows);
            _settingsStore.Save(Settings);
            DialogResult = true;
        }
        catch (Exception exception)
        {
            ShowMessage("保存设置失败：" + exception.Message, true);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // The settings window is modal; no background work is started here.
    }

    private void SelectPageSize(int pageSize)
    {
        foreach (var item in PageSizeComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag && int.TryParse(tag, out var value) && value == pageSize)
            {
                PageSizeComboBox.SelectedItem = item;
                return;
            }
        }
    }

    private int GetSelectedPageSize()
    {
        return PageSizeComboBox.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && int.TryParse(tag, out var value)
            ? value
            : 40;
    }

    private static List<string> ParseExtensions(string? value)
    {
        var result = new List<string>();
        foreach (var part in (value ?? string.Empty).Split([',', '，', ';', '；', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var extension = part.StartsWith('.') ? part : "." + part;
            if (extension.Length > 1 && !result.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(extension.ToLowerInvariant());
            }
        }

        return result;
    }

    private void ShowMessage(string message, bool isError)
    {
        SettingsMessage.Text = message;
        SettingsMessage.Foreground = isError
            ? (System.Windows.Media.Brush)FindResource("DangerBrush")
            : (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
    }

    private static AppSettings Clone(AppSettings source)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var clone = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(source, options), options) ?? new AppSettings();
        clone.Normalize();
        return clone;
    }
}

public sealed class DriveCheckItem : INotifyPropertyChanged
{
    private bool _isChecked;

    public string Path { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }

            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
