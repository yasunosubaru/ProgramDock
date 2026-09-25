using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ProgramDock.Services;

public sealed class HotKeyService : IDisposable
{
    private const int HotKeyId = 0x514C;
    private const int WmHotKey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private HwndSource? _source;
    private Action? _callback;
    private bool _registered;

    public bool IsRegistered => _registered;

    public bool Register(Window window, string gesture, Action callback)
    {
        Unregister();
        _callback = callback;
        if (!TryParse(gesture, out var modifiers, out var key))
        {
            return false;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
        var nativeModifiers = ToNativeModifiers(modifiers) | ModNoRepeat;
        _registered = RegisterHotKey(handle, HotKeyId, nativeModifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        if (!_registered)
        {
            _source?.RemoveHook(WndProc);
            _source = null;
        }

        return _registered;
    }

    public void Unregister()
    {
        if (_source is not null)
        {
            var handle = _source.Handle;
            if (_registered)
            {
                UnregisterHotKey(handle, HotKeyId);
            }

            _source.RemoveHook(WndProc);
            _source = null;
        }

        _registered = false;
        _callback = null;
    }

    public void Dispose() => Unregister();

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            _callback?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public static bool TryParse(string? gesture, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return false;
        }

        foreach (var part in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                case "option":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    if (!Enum.TryParse(part, ignoreCase: true, out key))
                    {
                        return false;
                    }

                    break;
            }
        }

        return modifiers != ModifierKeys.None && key != Key.None;
    }

    public static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        var result = 0u;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= ModAlt;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= ModControl;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= ModShift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= ModWin;
        return result;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
