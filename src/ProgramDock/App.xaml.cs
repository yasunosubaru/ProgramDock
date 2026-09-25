using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using ProgramDock.Services;

namespace ProgramDock;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;

    static App()
    {
        ProgramDock.Services.DpiAwareness.Enable();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.MigrateLegacyData();
        StartupService.RemoveLegacyRegistration();
        _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\ProgramDock.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // The first instance owns the tray/hotkey. Bring it forward for a
            // duplicate desktop launch, then exit before StartupUri creates a
            // second window.
            var existingWindow = FindWindow(null, "ProgramDock");
            if (existingWindow != IntPtr.Zero)
            {
                _ = ShowWindow(existingWindow, 9); // SW_RESTORE
                _ = SetForegroundWindow(existingWindow);
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Environment.Exit(0);
            return;
        }

        var culture = CultureInfo.CurrentUICulture;
        Thread.CurrentThread.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch
        {
            // A duplicate instance may exit without owning the mutex.
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        System.Windows.MessageBox.Show(
            $"程序遇到未处理的错误，但不会影响已保存的收藏和设置。\n\n{e.Exception.Message}",
            "ProgramDock",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteCrashLog(exception);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static void WriteCrashLog(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataDirectory);
            var file = AppPaths.CrashLogPath;
            var text = $"[{DateTime.Now:O}]\r\n{exception}\r\n\r\n";
            File.AppendAllText(file, text, Encoding.UTF8);
        }
        catch
        {
            // Logging must never mask the original exception.
        }
    }
}
