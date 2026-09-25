using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ProgramDock.Models;
using ProgramDock.Services;
using Forms = System.Windows.Forms;

namespace ProgramDock;

public partial class MainWindow : Window
{
    private readonly SettingsStore _settingsStore = new();
    private readonly CatalogCache _catalogCache = new();
    private readonly ProgramScanner _scanner = new();
    private readonly RegistryAppProvider _registryProvider = new();
    private readonly IconLoader _iconLoader = new();
    private readonly ObservableCollection<ProgramEntry> _visiblePrograms = [];
    private readonly Dictionary<string, ProgramEntry> _programsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _searchDebounce;
    private readonly HotKeyService _hotKey = new();
    private CancellationTokenSource? _scanCancellation;
    private Forms.NotifyIcon? _trayIcon;
    private AppSettings _settings;
    private IReadOnlyList<ProgramEntry> _filteredPrograms = [];
    private string _currentFilter = "Direct";
    private int _currentPage;
    private int _selectedIndex = -1;
    private bool _reallyExit;
    private bool _isScanning;
    private bool _hotKeyRegistered;
    private DateTime _lastCatalogRefresh = DateTime.MinValue;
    private int _lastScanErrors;
    private ShortcutEvidence _shortcutEvidence = new();
    private CancellationTokenSource? _searchCancellation;
    private int _filterVersion;

    public MainWindow()
    {
        _searchDebounce = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _searchDebounce.Tick += OnSearchDebounceTick;
        InitializeComponent();
        _settings = _settingsStore.Load();
        _settings.Normalize();
        ProgramItemsControl.ItemsSource = _visiblePrograms;
        SourceInitialized += OnSourceInitialized;
        SizeChanged += OnWindowSizeChanged;
        UpdateFilterButtons();
        UpdateSearchHint();
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
            // DragMove can briefly fail while a window is being restored.
        }
    }

    private void OnWindowMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || WindowState != WindowState.Normal)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject;
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase
                || source is System.Windows.Controls.TextBox
                || source is System.Windows.Controls.Primitives.ScrollBar
                || source is System.Windows.Controls.ComboBox
                || source is System.Windows.Controls.ListBox)
            {
                return;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
            // Ignore a drag that races with a window-state transition.
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Left = 20;
        Top = 20;
        UpdateContentSize();
        InitializeTrayIcon();
        RefreshShortcutEvidence();
        LoadCachedPrograms();
        UpdateFilterButtons();
        if (_settings.ScanRoots.Count == 0)
        {
            _settings.ScanRoots = GetDefaultRoots();
        }

        if (HasCommandLineFlag("--no-scan"))
        {
            LoadQuickPrograms();
            SetStatus("已跳过自动扫描（--no-scan）", false);
        }
        else if (_settings.ScanOnStartup)
        {
            _ = StartScanAsync();
        }
        else
        {
            LoadQuickPrograms();
            SetStatus("已加载上次索引；可点击“重新扫描”", false);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        RegisterGlobalHotKey();
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateContentSize();
    }

    private void UpdateContentSize()
    {
        if (ActualWidth > 0 && ActualHeight > 0)
        {
            // The border contributes 10 DIP on each side and the content grid
            // contributes its own 42/28/42/24 margin. Explicitly constrain it
            // so a virtualized/large ItemsControl cannot widen a maximized WPF
            // window and push the header controls off-screen.
            var dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var compensation = dpiScale > 1.05 ? Math.Min(1.0, 1.2 / dpiScale) : 1.0;
            ContentGrid.Width = Math.Max(320, (ActualWidth - 104) / compensation);
            ContentGrid.Height = Math.Max(420, (ActualHeight - 72) * 0.92 / compensation);
            ContentGrid.LayoutTransform = new ScaleTransform(compensation, compensation);

        }
    }

    private async Task StartScanAsync()
    {
        if (_isScanning)
        {
            return;
        }

        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        _lastScanErrors = 0;
        _isScanning = true;
        SetScanVisual(true);
        SetStatus("正在准备扫描…", true);

        try
        {
            // Registry entries appear quickly and make the first window useful
            // while the recursive disk scan continues in the background.
            var quickPrograms = await Task.Run(_registryProvider.Discover);
            AddEntries(quickPrograms);
            ApplyFilter();
            await LoadIconsAsync(_visiblePrograms.ToArray());

            var roots = GetScanRoots();
            if (roots.Count == 0)
            {
                SetStatus("没有可扫描的固定磁盘，请在设置中选择扫描范围", false);
                return;
            }

            var progress = new Progress<ScanProgress>(OnScanProgress);
            var scannedPrograms = await _scanner.ScanAsync(
                roots,
                _settings.Extensions,
                progress,
                _scanCancellation.Token);
            AddEntries(scannedPrograms);
            _ = InspectCachedCommandLineToolsAsync(_programsByPath.Values.ToArray());
            _settings.LastScanAt = DateTime.Now;
            _settingsStore.Save(_settings);
            SaveCatalog();
            ApplyFilter();
            await LoadIconsAsync(_visiblePrograms.ToArray());
            SetStatus($"扫描完成 · {_settings.ScanRoots.Count} 个范围 · {_lastScanErrors} 个跳过/错误", false);
        }
        catch (OperationCanceledException)
        {
            SetStatus("扫描已取消，保留已发现的程序", false);
        }
        catch (Exception exception)
        {
            SetStatus("扫描遇到错误：" + exception.Message, false);
        }
        finally
        {
            SaveCatalog();
            _isScanning = false;
            _scanCancellation?.Dispose();
            _scanCancellation = null;
            SetScanVisual(false);
        }
    }

    private void OnScanProgress(ScanProgress progress)
    {
        _lastScanErrors = progress.Errors;
        var current = progress.CurrentPath;
        if (current.Length > 72)
        {
            current = "…" + current[^72..];
        }

        var prefix = $"扫描中 · {progress.Matches} 个程序 · {progress.FilesSeen} 个文件";
        SetStatus(string.IsNullOrWhiteSpace(current) ? prefix : $"{prefix} · {current}", true);

        if (progress.Batch.Count > 0)
        {
            AddEntries(progress.Batch);
            if ((DateTime.UtcNow - _lastCatalogRefresh).TotalMilliseconds > 900)
            {
                _lastCatalogRefresh = DateTime.UtcNow;
                _ = ApplyFilterAsync();
            }
        }
    }

    private void LoadQuickPrograms()
    {
        try
        {
            AddEntries(_registryProvider.Discover());
            ApplyFilter();
            _ = LoadIconsAsync(_visiblePrograms.ToArray());
        }
        catch
        {
            SetStatus("读取已安装程序失败，可稍后重新扫描", false);
        }
    }

    private void RefreshShortcutEvidence()
    {
        try
        {
            _shortcutEvidence = ShortcutEvidenceProvider.Discover();
        }
        catch
        {
            _shortcutEvidence = new ShortcutEvidence();
        }
    }

    private void LoadCachedPrograms()
    {
        var cached = _catalogCache.Load();
        if (cached.Count == 0)
        {
            return;
        }

        AddEntries(cached);
        ApplyFilter();
        _ = InspectCachedCommandLineToolsAsync(_programsByPath.Values.ToArray());
        _ = LoadIconsAsync(_visiblePrograms.ToArray());
        SetStatus($"已加载上次索引 · {cached.Count} 个程序", false);
    }

    private async Task InspectCachedCommandLineToolsAsync(IReadOnlyList<ProgramEntry> entries)
    {
        var inspected = 0;
        foreach (var entry in entries)
        {
            var extension = Path.GetExtension(entry.Path).ToLowerInvariant();
            if (extension is not (".exe" or ".com") || entry.IsCommandLineTool)
            {
                continue;
            }

            var isConsole = await Task.Run(() => ExecutableInspector.IsConsoleExecutable(entry.Path));
            if (isConsole)
            {
                entry.IsCommandLineTool = true;
                ApplyClassification(entry);
            }

            inspected++;
            if (inspected % 500 == 0)
            {
                ApplyFilter();
                await Task.Yield();
            }
        }

        ApplyFilter();
        var snapshot = _programsByPath.Values
            .Where(entry => !_settings.HiddenPaths.Contains(entry.Path))
            .ToArray();
        await Task.Run(() => _catalogCache.Save(snapshot));
    }

    private void SaveCatalog()
    {
        _catalogCache.Save(_programsByPath.Values.Where(entry => !_settings.HiddenPaths.Contains(entry.Path)));
    }

    private void AddEntries(IEnumerable<ProgramEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Path))
            {
                continue;
            }

            if (!_settings.ShowHiddenPrograms && _settings.HiddenPaths.Contains(entry.Path))
            {
                continue;
            }

            entry.IsFavorite = _settings.FavoritePaths.Contains(entry.Path);
            entry.LastLaunchedAt = _settings.RecentPrograms
                .FirstOrDefault(item => string.Equals(item.Path, entry.Path, StringComparison.OrdinalIgnoreCase))?
                .LastLaunchedAt;
            ApplyClassification(entry);

            if (_programsByPath.TryGetValue(entry.Path, out var existing))
            {
                if (string.IsNullOrWhiteSpace(existing.Description) && !string.IsNullOrWhiteSpace(entry.Description))
                {
                    existing.Description = entry.Description;
                }

                existing.IsSystemComponent |= entry.IsSystemComponent;
                existing.IsCommandLineTool |= entry.IsCommandLineTool;
                if (existing.Source == ProgramSource.Disk && entry.Source == ProgramSource.Installed)
                {
                    existing.Name = entry.Name;
                }

                ApplyClassification(existing);
                continue;
            }

            _programsByPath[entry.Path] = entry;
        }
    }

    private void ApplyClassification(ProgramEntry entry)
    {
        var result = ProgramClassifier.Classify(entry, _settings, _shortcutEvidence);
        var hasManualOverride = _settings.CategoryOverrides.ContainsKey(entry.Path)
            || _settings.DirectUsePaths.Contains(entry.Path)
            || _settings.SystemSupportPaths.Contains(entry.Path);
        entry.ApplyClassification(result.Category, result.Reason, result.Category, hasManualOverride);
    }

    private List<string> GetScanRoots()
    {
        var roots = _settings.ScanRoots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return roots;
    }

    private static List<string> GetDefaultRoots()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
                .Select(drive => drive.RootDirectory.FullName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [Environment.GetEnvironmentVariable("SystemDrive") is { Length: > 0 } drive ? drive + "\\" : "C:\\"];
        }
    }

    private void ApplyFilter()
    {
        _filterVersion++;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;

        var source = BuildSearchSource();
        _filteredPrograms = SearchEngine.Search(source, SearchBox.Text, _currentFilter).ToList();
        ResetFilterPosition();
        RenderPage();
    }

    private async Task ApplyFilterAsync()
    {
        var version = ++_filterVersion;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;
        var source = BuildSearchSource().ToArray();
        var query = SearchBox.Text;
        var filter = _currentFilter;

        try
        {
            var result = await Task.Run(
                () => SearchEngine.Search(source, query, filter).ToList(),
                token);
            if (token.IsCancellationRequested || version != _filterVersion)
            {
                return;
            }

            _filteredPrograms = result;
            ResetFilterPosition();
            RenderPage();
        }
        catch (OperationCanceledException)
        {
            // A newer search request superseded this one.
        }
    }

    private IReadOnlyList<ProgramEntry> BuildSearchSource()
    {
        IEnumerable<ProgramEntry> source = _programsByPath.Values;
        if (!_settings.ShowHiddenPrograms)
        {
            source = source.Where(entry => !_settings.HiddenPaths.Contains(entry.Path));
        }

        if (_currentFilter == "Direct")
        {
            source = SelectDirectEntries(source);
        }

        return source.ToArray();
    }

    private void ResetFilterPosition()
    {
        var oldSelectedPath = _selectedIndex >= 0 && _selectedIndex < _filteredPrograms.Count
            ? _filteredPrograms[_selectedIndex].Path
            : null;
        _currentPage = 0;
        _selectedIndex = -1;
        if (oldSelectedPath is not null)
        {
            _selectedIndex = _filteredPrograms.ToList().FindIndex(entry => entry.Path == oldSelectedPath);
        }
    }

    private IEnumerable<ProgramEntry> SelectDirectEntries(IEnumerable<ProgramEntry> source)
    {
        var directEntries = source
            .Where(entry => entry.Category == ProgramCategory.DirectUse)
            .ToList();
        var shortcutEntries = directEntries
            .Where(entry => entry.Source == ProgramSource.Shortcut && _shortcutEvidence.Matches(entry))
            .ToList();
        var targetPaths = _shortcutEvidence.TargetPaths;
        var extras = directEntries.Where(entry =>
            entry.Source != ProgramSource.Shortcut
            && (!targetPaths.Contains(entry.Path) || entry.HasManualCategoryOverride));

        return shortcutEntries
            .Concat(extras)
            .GroupBy(entry => entry.Source == ProgramSource.Shortcut
                ? _shortcutEvidence.GetGroupKey(entry)
                : "program:" + NormalizeProgramName(entry.Name), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(entry => entry.IsFavorite)
                .ThenBy(entry => entry.Source == ProgramSource.Shortcut ? 0 : 1)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .First());
    }

    private static string NormalizeProgramName(string? name)
    {
        return string.Concat((name ?? string.Empty)
            .Where(character => char.IsLetterOrDigit(character))
            .Select(character => char.ToUpperInvariant(character)));
    }

    private void RenderPage()
    {
        var pageSize = Math.Max(1, _settings.PageSize);
        var pageCount = Math.Max(1, (int)Math.Ceiling(_filteredPrograms.Count / (double)pageSize));
        _currentPage = Math.Clamp(_currentPage, 0, pageCount - 1);
        var start = _currentPage * pageSize;
        var pageItems = _filteredPrograms.Skip(start).Take(pageSize).ToArray();

        foreach (var entry in _visiblePrograms)
        {
            entry.IsSelected = false;
        }

        ProgramItemsControl.Width = Math.Max(320, ContentGrid.ActualWidth);
        ProgramItemsControl.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        _visiblePrograms.Clear();
        foreach (var entry in pageItems)
        {
            _visiblePrograms.Add(entry);
        }

        if (_selectedIndex < start || _selectedIndex >= start + pageItems.Length)
        {
            _selectedIndex = pageItems.Length > 0 ? start : -1;
        }

        if (_selectedIndex >= start && _selectedIndex < start + pageItems.Length)
        {
            _visiblePrograms[_selectedIndex - start].IsSelected = true;
        }

        EmptyState.Visibility = _filteredPrograms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageText.Text = $"{_currentPage + 1} / {pageCount}";
        ResultCountText.Text = $"{_filteredPrograms.Count} 个";
        PreviousPageButton.IsEnabled = _currentPage > 0;
        NextPageButton.IsEnabled = _currentPage < pageCount - 1;
        if (pageItems.Length > 0)
        {
            _ = LoadIconsAsync(pageItems);
        }
    }

    private async Task LoadIconsAsync(IReadOnlyList<ProgramEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.Icon is not null)
            {
                continue;
            }

            var icon = await Task.Run(() => _iconLoader.Load(entry.IconPath));
            entry.Icon = icon;
            await Task.Yield();
        }
    }

    private void OnProgramClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProgramEntry entry })
        {
            LaunchEntry(entry);
        }
    }

    private void LaunchEntry(ProgramEntry entry)
    {
        if (IsScript(entry.Path) && !ConfirmScriptLaunch(entry))
        {
            return;
        }

        if (!LaunchService.TryLaunch(entry, out var error))
        {
            SetStatus("无法启动：" + (error ?? "未知错误"), false);
            System.Windows.MessageBox.Show(this, error ?? "无法启动该程序。", "ProgramDock", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        entry.LastLaunchedAt = DateTime.Now;
        _settings.AddRecent(entry);
        _settingsStore.Save(_settings);
        SetStatus("已启动 " + entry.Name, false);
        if (_settings.HideAfterLaunch)
        {
            HideLauncher();
        }
        else
        {
            ApplyFilter();
        }
    }

    private static bool IsScript(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".bat" or ".cmd" or ".ps1" or ".vbs" or ".js";
    }

    private bool ConfirmScriptLaunch(ProgramEntry entry)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            $"即将运行脚本/批处理文件：\n\n{entry.Path}\n\n确定要继续吗？",
            "确认运行脚本",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProgramEntry entry })
        {
            var isFavorite = sender is System.Windows.Controls.Primitives.ToggleButton toggle
                ? toggle.IsChecked == true
                : !entry.IsFavorite;
            entry.IsFavorite = isFavorite;
            if (isFavorite)
            {
                _settings.FavoritePaths.Add(entry.Path);
            }
            else
            {
                _settings.FavoritePaths.Remove(entry.Path);
            }

            _settingsStore.Save(_settings);
            ApplyFilter();
        }

        e.Handled = true;
    }

    private void OnContextLaunchClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProgramEntry entry)
        {
            LaunchEntry(entry);
        }
    }

    private void OnContextFavoriteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProgramEntry entry)
        {
            entry.IsFavorite = !entry.IsFavorite;
            if (entry.IsFavorite)
            {
                _settings.FavoritePaths.Add(entry.Path);
            }
            else
            {
                _settings.FavoritePaths.Remove(entry.Path);
            }

            _settingsStore.Save(_settings);
            ApplyFilter();
        }
    }

    private void OnContextMarkDirectClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProgramEntry entry)
        {
            SetCategoryOverride(entry, ProgramCategory.DirectUse);
        }
    }

    private void OnContextMarkSystemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProgramEntry entry)
        {
            SetCategoryOverride(entry, ProgramCategory.SystemSupport);
        }
    }

    private void OnContextResetCategoryClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProgramEntry entry)
        {
            _settings.ClearCategoryOverride(entry.Path);
            ApplyClassification(entry);
            _settingsStore.Save(_settings);
            ApplyFilter();
            SetStatus("已恢复自动分类：" + entry.Name, false);
        }
    }

    private void SetCategoryOverride(ProgramEntry entry, ProgramCategory category)
    {
        _settings.SetCategoryOverride(entry.Path, category);
        ApplyClassification(entry);
        _settingsStore.Save(_settings);
        ApplyFilter();
        SetStatus("已归类为" + entry.CategoryLabel + "：" + entry.Name, false);
    }

    private void OnContextFolderClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProgramEntry entry)
        {
            LaunchService.OpenFolder(entry.Path);
        }
    }

    private void OnContextCopyClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProgramEntry entry)
        {
            try
            {
                System.Windows.Clipboard.SetText(entry.Path);
                SetStatus("已复制完整路径", false);
            }
            catch
            {
                SetStatus("复制失败，请手动复制路径", false);
            }
        }
    }

    private void OnContextHideClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProgramEntry entry)
        {
            _settings.HiddenPaths.Add(entry.Path);
            _settingsStore.Save(_settings);
            _programsByPath.Remove(entry.Path);
            ApplyFilter();
            SetStatus("已从索引中隐藏 " + entry.Name, false);
        }
    }

    private void OnFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string tag })
        {
            _currentFilter = tag;
            _currentPage = 0;
            _selectedIndex = -1;
            UpdateFilterButtons();
            ApplyFilter();
        }
    }

    private void UpdateFilterButtons()
    {
        var buttons = new[]
        {
            AllFilterButton,
            DirectFilterButton,
            SystemFilterButton,
            FavoritesFilterButton,
            RecentFilterButton
        };
        foreach (var button in buttons)
        {
            var selected = string.Equals(button.Tag as string, _currentFilter, StringComparison.Ordinal);
            button.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(selected ? "#1D5D50" : "#1B2634")!;
            button.BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(selected ? "#61D0AA" : "#3A4B60")!;
            button.Foreground = selected ? (System.Windows.Media.Brush)FindResource("AccentStrongBrush") : (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchHint();
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void OnSearchDebounceTick(object? sender, EventArgs e)
    {
        _searchDebounce.Stop();
        _ = ApplyFilterAsync();
    }

    private void OnSearchKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            LaunchSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.Text.Length > 0)
        {
            SearchBox.Clear();
            e.Handled = true;
        }
    }

    private void OnClearSearchClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void UpdateSearchHint()
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnRescanClick(object sender, RoutedEventArgs e)
    {
        if (_isScanning)
        {
            _scanCancellation?.Cancel();
            SetStatus("正在停止扫描…", true);
            return;
        }

        RefreshShortcutEvidence();
        foreach (var entry in _programsByPath.Values)
        {
            ApplyClassification(entry);
        }

        ApplyFilter();
        _ = StartScanAsync();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var wasTopmost = Topmost;
        Topmost = false;
        var settingsWindow = new SettingsWindow(_settings, _settingsStore)
        {
            Owner = this
        };
        var accepted = settingsWindow.ShowDialog() == true;
        Topmost = wasTopmost;
        if (accepted)
        {
            _settings = settingsWindow.Settings;
            _settings.Normalize();
            StartupService.SetEnabled(_settings.StartWithWindows);
            _settingsStore.Save(_settings);
            RegisterGlobalHotKey();
            ApplyFilter();
        }
    }

    private void OnPreviousPageClick(object sender, RoutedEventArgs e)
    {
        ChangePage(-1);
    }

    private void OnNextPageClick(object sender, RoutedEventArgs e)
    {
        ChangePage(1);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Math.Abs(e.Delta) < 120)
        {
            return;
        }

        ChangePage(e.Delta < 0 ? 1 : -1);
        e.Handled = true;
    }

    private void ChangePage(int delta)
    {
        var pageCount = Math.Max(1, (int)Math.Ceiling(_filteredPrograms.Count / (double)Math.Max(1, _settings.PageSize)));
        var next = Math.Clamp(_currentPage + delta, 0, pageCount - 1);
        if (next == _currentPage)
        {
            return;
        }

        _currentPage = next;
        _selectedIndex = next * _settings.PageSize;
        RenderPage();
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (SearchBox.IsKeyboardFocusWithin && SearchBox.Text.Length > 0)
            {
                SearchBox.Clear();
            }
            else
            {
                HideLauncher();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            if (!_isScanning)
            {
                _ = StartScanAsync();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FocusSearch();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            LaunchSelected();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            MoveSelection(e.Key);
            e.Handled = true;
        }
    }

    private void MoveSelection(Key key)
    {
        if (_visiblePrograms.Count == 0)
        {
            return;
        }

        var current = _selectedIndex < 0 ? 0 : _selectedIndex;
        var delta = key switch
        {
            Key.Left => -1,
            Key.Right => 1,
            Key.Up => -8,
            Key.Down => 8,
            _ => 0
        };
        var next = Math.Clamp(current + delta, _currentPage * _settings.PageSize, _currentPage * _settings.PageSize + _visiblePrograms.Count - 1);
        _selectedIndex = next;
        foreach (var item in _visiblePrograms)
        {
            item.IsSelected = false;
        }

        _visiblePrograms[next - _currentPage * _settings.PageSize].IsSelected = true;
        var container = ProgramItemsControl.ItemContainerGenerator.ContainerFromIndex(next - _currentPage * _settings.PageSize) as FrameworkElement;
        container?.BringIntoView();
    }

    private void LaunchSelected()
    {
        if (_selectedIndex >= 0 && _selectedIndex < _filteredPrograms.Count)
        {
            LaunchEntry(_filteredPrograms[_selectedIndex]);
        }
        else if (_visiblePrograms.Count > 0)
        {
            LaunchEntry(_visiblePrograms[0]);
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        HideLauncher();
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        _reallyExit = true;
        _scanCancellation?.Cancel();
        _hotKey.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_reallyExit && _settings.CloseToTray)
        {
            e.Cancel = true;
            HideLauncher();
            return;
        }

        _scanCancellation?.Cancel();
        _hotKey.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        if (!_isScanning)
        {
            SaveCatalog();
        }

        _reallyExit = true;
        System.Windows.Application.Current.Shutdown();
    }

    private void RegisterGlobalHotKey()
    {
        _hotKeyRegistered = _hotKey.Register(this, _settings.GlobalHotkey, ToggleLauncher);
        if (!_hotKeyRegistered && !string.IsNullOrWhiteSpace(_settings.GlobalHotkey))
        {
            SetStatus("全局快捷键注册失败，可能已被其他程序占用", false);
        }
    }

    private void ToggleLauncher()
    {
        if (IsVisible)
        {
            HideLauncher();
        }
        else
        {
            ShowLauncher();
        }
    }

    private void ShowLauncher()
    {
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Left = 20;
        Top = 20;
        Show();
        Topmost = true;
        Activate();
        FocusSearch();
    }

    private void HideLauncher()
    {
        Topmost = false;
        ShowInTaskbar = false;
        Hide();
    }

    private void FocusSearch()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }), DispatcherPriority.Input);
    }

    private void SetScanVisual(bool scanning)
    {
        ScanProgress.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        RescanButton.IsEnabled = true;
        RescanButton.Content = scanning ? "停止扫描" : "重新扫描";
        StatusDot.Fill = (System.Windows.Media.Brush)FindResource(scanning ? "AccentBrush" : "TextMutedBrush");
    }

    private void SetStatus(string text, bool scanning)
    {
        ScanStatusText.Text = text;
        StatusDot.Fill = (System.Windows.Media.Brush)FindResource(scanning ? "AccentBrush" : "TextMutedBrush");
    }

    private void InitializeTrayIcon()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 ProgramDock", null, (_, _) => Dispatcher.Invoke(ToggleLauncher));
        menu.Items.Add("重新扫描", null, (_, _) => Dispatcher.Invoke(() =>
        {
            ShowLauncher();
            if (!_isScanning)
            {
                _ = StartScanAsync();
            }
        }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(() => OnExitClick(this, new RoutedEventArgs())));

        System.Drawing.Icon? trayImage = null;
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            try
            {
                trayImage = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
            }
            catch
            {
                // Fall back to the system application icon.
            }
        }

        trayImage ??= System.Drawing.SystemIcons.Application;
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = trayImage,
            Text = "ProgramDock",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleLauncher);
    }

    private static bool HasCommandLineFlag(string flag)
    {
        return Environment.GetCommandLineArgs().Any(argument => argument.Equals(flag, StringComparison.OrdinalIgnoreCase));
    }
}
