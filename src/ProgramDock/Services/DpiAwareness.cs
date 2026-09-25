using System;
using System.Runtime.InteropServices;

namespace ProgramDock.Services;

internal static class DpiAwareness
{
    private static readonly IntPtr PerMonitorV2 = new(-4);

    public static void Enable()
    {
        try
        {
            if (SetProcessDpiAwarenessContext(PerMonitorV2))
            {
                return;
            }
        }
        catch
        {
            // Fall through to the older APIs on older Windows builds.
        }

        try
        {
            _ = SetProcessDpiAwarenessContext(new IntPtr(-3)); // PerMonitor
        }
        catch
        {
            try
            {
                _ = SetProcessDPIAware();
            }
            catch
            {
                // DPI awareness is an enhancement; the app can still run without it.
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDPIAware();
}
