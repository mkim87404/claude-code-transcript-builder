using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TranscriptBuilder;

// Matches a window's native title bar to the app's current light/dark theme.
internal static class TitleBarTheme
{
    // Microsoft Learn (DWMWINDOWATTRIBUTE): DWMWA_USE_IMMERSIVE_DARK_MODE = 20, supported starting
    // with Windows 11 Build 22000.
    private const int DwmwaUseImmersiveDarkMode = 20;

    // ThemeMode restyles a window's content but not the OS-drawn title bar, which needs this separate
    // DWM call. Needs the window's HWND, so call it from OnSourceInitialized or later. A harmless
    // no-op (non-zero HRESULT, ignored) on Windows versions older than the attribute's minimum.
    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var useImmersiveDarkMode = Application.Current.ThemeMode == ThemeMode.Dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useImmersiveDarkMode, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
