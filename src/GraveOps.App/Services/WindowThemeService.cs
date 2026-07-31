using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GraveOps.App.Services;

public static class WindowThemeService
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int value,
        int valueSize);

    public static void Apply(Window window)
    {
        if (window.WindowStyle == WindowStyle.None)
            return;

        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == nint.Zero)
                return;

            var enabled = 1;
            var size = Marshal.SizeOf<int>();

            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, size) != 0)
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref enabled, size);
        }
        catch
        {
            // Presentation fallback only; never affect GraveOps behavior.
        }
    }
}