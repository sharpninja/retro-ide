using System.Runtime.InteropServices;
using Avalonia;

namespace RetroIde.Workbench;

internal static partial class DesktopPlatform
{
    internal static AppBuilder UseAvailableDesktop(this AppBuilder builder)
    {
        builder.UsePlatformDetect();
        if (OperatingSystem.IsLinux() && HasWaylandDisplay() && !CanOpenX11Display())
        {
            builder.With(new WaylandPlatformOptions { AppId = "retro-ide" });
            builder.UseWaylandWithFallback();
        }
        return builder;
    }

    private static bool HasWaylandDisplay() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_SOCKET"));

    private static bool CanOpenX11Display()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
            return false;

        // Check XWayland without initializing Xlib before Avalonia sets up its threading.
        try
        {
            var connection = Connect(0, 0);
            if (connection == 0)
                return false;
            try { return ConnectionHasError(connection) == 0; }
            finally { Disconnect(connection); }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [LibraryImport("libxcb.so.1", EntryPoint = "xcb_connect")]
    private static partial nint Connect(nint displayName, nint screen);

    [LibraryImport("libxcb.so.1", EntryPoint = "xcb_connection_has_error")]
    private static partial int ConnectionHasError(nint connection);

    [LibraryImport("libxcb.so.1", EntryPoint = "xcb_disconnect")]
    private static partial void Disconnect(nint connection);
}
