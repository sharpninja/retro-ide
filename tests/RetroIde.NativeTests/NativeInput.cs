using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace RetroIde.NativeTests;

// Uses native input on a dedicated test display or an available Windows desktop.
internal sealed partial class NativeInput : IDisposable
{
    private readonly IntPtr _display;
    private bool _buttonPressed;

    public NativeInput()
    {
        if (OperatingSystem.IsLinux())
        {
            _display = XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero)
                throw new InvalidOperationException("The native Linux test requires an X11 display with XTEST.");
        }
        else if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Run this test on Linux or Windows.");
        }
    }

    public string Backend => OperatingSystem.IsWindows() ? "Win32" : "X11 / XTEST";

    public async Task ClickAsync(PixelPoint target)
    {
        await MoveAsync(target);
        SetButton(pressed: true);
        await Task.Delay(40).ConfigureAwait(true);
        await ReleaseAsync();
    }

    public async Task DragStartAsync(PixelPoint origin)
    {
        await MoveAsync(origin);
        SetButton(pressed: true);
        await Task.Delay(60).ConfigureAwait(true);
        await MoveAsync(new PixelPoint(origin.X + 10, origin.Y));
        await MoveAsync(new PixelPoint(origin.X + 15, origin.Y + 50));
    }

    public async Task MoveAsync(PixelPoint target)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!SetCursorPos(target.X, target.Y))
                throw new InvalidOperationException($"Could not move the Win32 cursor to {target} (error {Marshal.GetLastPInvokeError()}).");
        }
        else
        {
            XTestFakeMotionEvent(_display, -1, target.X, target.Y, 0);
            XFlush(_display);
        }

        await Task.Delay(85).ConfigureAwait(true);
        if (OperatingSystem.IsWindows())
        {
            if (!GetCursorPos(out var position))
                throw new InvalidOperationException($"Could not read the Win32 cursor (error {Marshal.GetLastPInvokeError()}).");
            var actual = new PixelPoint(position.X, position.Y);
            if (actual != target)
                throw new InvalidOperationException($"Win32 cursor is at {actual} after moving to {target}.");
        }
    }

    public async Task ReleaseAsync()
    {
        SetButton(pressed: false);
        await Task.Delay(120).ConfigureAwait(true);
    }

    public void FocusWindow(Window window)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("Missing native handle.");
        window.Activate();
        if (OperatingSystem.IsLinux())
            FocusWindow(handle);
    }

    public static bool IsWin32WindowAt(PixelPoint position, Window window)
    {
        var hit = WindowFromPoint(new CursorPoint { X = position.X, Y = position.Y });
        return hit != 0 && GetAncestor(hit, 2) == window.TryGetPlatformHandle()?.Handle;
    }

    public void FocusWindow(nint handle)
    {
        if (OperatingSystem.IsWindows())
        {
            if (GetForegroundWindow() != handle && !SetForegroundWindow(handle))
                throw new InvalidOperationException("Could not activate the production workbench on the interactive desktop.");
        }
        else
        {
            // Xvfb has no window manager to assign keyboard focus.
            XSetInputFocus(_display, handle, 2, 0);
            XFlush(_display);
        }
    }

    public nint FindWindow(Process process)
    {
        if (OperatingSystem.IsWindows())
        {
            process.Refresh();
            return process.MainWindowHandle;
        }

        return FindX11Window(XDefaultRootWindow(_display), XInternAtom(_display, "_NET_WM_PID", 0), process.Id);
    }

    private nint FindX11Window(nint window, nint pidAtom, int processId)
    {
        if (XGetWindowProperty(_display, window, pidAtom, 0, 1, 0, 6, out _, out var format, out var count, out _, out var value) == 0 &&
            value != 0)
        {
            try
            {
                if (format == 32 && count == 1 && Marshal.ReadInt32(value) == processId)
                    return window;
            }
            finally { XFree(value); }
        }

        if (XQueryTree(_display, window, out _, out _, out var children, out var childCount) == 0 || children == 0)
            return 0;
        try
        {
            for (var index = 0; index < childCount; index++)
            {
                var match = FindX11Window(Marshal.ReadIntPtr(children, index * IntPtr.Size), pidAtom, processId);
                if (match != 0)
                    return match;
            }
            return 0;
        }
        finally { XFree(children); }
    }

    public unsafe void CloseWindow(nint window)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!PostMessageW(window, 0x0010, 0, 0)) // WM_CLOSE
                throw new InvalidOperationException("Could not request native Win32 window close.");
            return;
        }

        // XEvent is a union of 24 native longs. Send the same close protocol as an X11 window manager.
        nint* buffer = stackalloc nint[24];
        new Span<nint>(buffer, 24).Clear();
        var message = (XClientMessage*)buffer;
        message->Type = 33; // ClientMessage
        message->Display = _display;
        message->Window = window;
        message->MessageType = XInternAtom(_display, "WM_PROTOCOLS", 0);
        message->Format = 32;
        message->FirstData = XInternAtom(_display, "WM_DELETE_WINDOW", 0);
        if (XSendEvent(_display, window, 0, 0, (nint)buffer) == 0)
            throw new InvalidOperationException("Could not request native X11 window close.");
        XFlush(_display);
    }

    public long ReadCursor()
    {
        if (OperatingSystem.IsWindows())
        {
            var cursor = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
            if (!GetCursorInfo(ref cursor) || (cursor.Flags & 1) == 0)
                throw new InvalidOperationException("Could not read the visible Win32 pointer cursor.");
            return cursor.Handle.ToInt64();
        }

        var image = XFixesGetCursorImage(_display);
        if (image == 0)
            throw new InvalidOperationException("Could not read the visible X11 pointer cursor with XFixes.");
        try
        {
            var cursor = Marshal.PtrToStructure<XCursorImage>(image);
            var hash = new HashCode();
            hash.Add(cursor.Width);
            hash.Add(cursor.Height);
            hash.Add(cursor.HotX);
            hash.Add(cursor.HotY);
            for (var index = 0; index < cursor.Width * cursor.Height; index++)
                hash.Add(Marshal.ReadIntPtr(cursor.Pixels, index * IntPtr.Size));
            return hash.ToHashCode();
        }
        finally { XFree(image); }
    }

    public async Task ShortcutAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var key in new byte[] { 0x11, 0x10, 0x46 })
                keybd_event(key, 0, 0, 0);
            foreach (var key in new byte[] { 0x46, 0x10, 0x11 })
                keybd_event(key, 0, 2, 0);
        }
        else
        {
            foreach (var key in new nuint[] { 0xffe3, 0xffe1, 0x66 })
                XTestFakeKeyEvent(_display, XKeysymToKeycode(_display, key), 1, 0);
            foreach (var key in new nuint[] { 0x66, 0xffe1, 0xffe3 })
                XTestFakeKeyEvent(_display, XKeysymToKeycode(_display, key), 0, 0);
            XFlush(_display);
        }
        await Task.Delay(120).ConfigureAwait(true);
    }

    public void Dispose()
    {
        if (_buttonPressed)
            SetButton(pressed: false);
        if (_display != IntPtr.Zero)
            XCloseDisplay(_display);
    }

    private void SetButton(bool pressed)
    {
        if (OperatingSystem.IsWindows())
            mouse_event(pressed ? 0x0002u : 0x0004u, 0, 0, 0, 0);
        else
        {
            XTestFakeButtonEvent(_display, 1, pressed ? 1 : 0, 0);
            XFlush(_display);
        }
        _buttonPressed = pressed;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out CursorPoint point);

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(CursorPoint point);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint window, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size;
        public int Flags;
        public nint Handle;
        public CursorPoint Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XCursorImage
    {
        public short X;
        public short Y;
        public ushort Width;
        public ushort Height;
        public ushort HotX;
        public ushort HotY;
        public nuint Serial;
        public nint Pixels;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XClientMessage
    {
        public int Type;
        public nuint Serial;
        public int Sent;
        public nint Display;
        public nint Window;
        public nint MessageType;
        public int Format;
        public nint FirstData;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorInfo(ref CursorInfo cursor);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("libXfixes.so.3")]
    private static partial nint XFixesGetCursorImage(nint display);

    [LibraryImport("libX11.so.6")]
    private static partial nint XDefaultRootWindow(nint display);

    [LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint XInternAtom(nint display, string name, int onlyIfExists);

    [LibraryImport("libX11.so.6")]
    private static partial int XQueryTree(nint display, nint window, out nint root, out nint parent, out nint children, out uint count);

    [LibraryImport("libX11.so.6")]
    private static partial int XGetWindowProperty(nint display, nint window, nint property, nint offset, nint length, int delete,
        nint requestedType, out nint actualType, out int format, out nuint count, out nuint remaining, out nint value);

    [LibraryImport("libX11.so.6")]
    private static partial int XSendEvent(nint display, nint window, int propagate, nint eventMask, nint eventBuffer);

    [LibraryImport("libX11.so.6")]
    private static partial int XFree(nint value);

    [LibraryImport("user32.dll")]
    private static partial void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);

    [LibraryImport("user32.dll")]
    private static partial void keybd_event(byte key, byte scan, uint flags, nuint extra);

    [LibraryImport("libX11.so.6")]
    private static partial IntPtr XOpenDisplay(IntPtr name);

    [LibraryImport("libX11.so.6")]
    private static partial int XCloseDisplay(IntPtr display);

    [LibraryImport("libX11.so.6")]
    private static partial int XFlush(IntPtr display);

    [LibraryImport("libX11.so.6")]
    private static partial int XSetInputFocus(IntPtr display, IntPtr window, int revert, nuint time);

    [LibraryImport("libX11.so.6")]
    private static partial byte XKeysymToKeycode(IntPtr display, nuint key);

    [LibraryImport("libXtst.so.6")]
    private static partial int XTestFakeMotionEvent(IntPtr display, int screen, int x, int y, nuint delay);

    [LibraryImport("libXtst.so.6")]
    private static partial int XTestFakeButtonEvent(IntPtr display, uint button, int pressed, nuint delay);

    [LibraryImport("libXtst.so.6")]
    private static partial int XTestFakeKeyEvent(IntPtr display, uint key, int pressed, nuint delay);
}
