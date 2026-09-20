using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SniPro.Windows;

public sealed class GlobalHotkeyHost : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const int HotkeyId = 0x534E;
    private const int SwHide = 0;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;

    private readonly HwndSource _messageSource;
    private bool _registered;
    private bool _disposed;

    public GlobalHotkeyHost()
    {
        var parameters = new HwndSourceParameters("SniPro.GlobalHotkey")
        {
            Width = 1,
            Height = 1,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = WsPopup,
            ExtendedWindowStyle = WsExToolWindow
        };

        _messageSource = new HwndSource(parameters);
        _messageSource.AddHook(WindowProc);
        NativeMethods.ShowWindow(_messageSource.Handle, SwHide);
    }

    public event EventHandler? HotkeyPressed;

    public string? RegisteredHotkey { get; private set; }

    public bool TryRegister(string? text, out string error)
    {
        ThrowIfDisposed();

        if (!GlobalHotkeyParser.TryParse(text, out var definition, out error))
        {
            return false;
        }

        Unregister();

        var modifiers = definition.Modifiers | GlobalHotkeyParser.ModNoRepeat;
        if (!NativeMethods.RegisterHotKey(
                _messageSource.Handle,
                HotkeyId,
                modifiers,
                definition.VirtualKey))
        {
            var nativeError = Marshal.GetLastWin32Error();
            error = new Win32Exception(nativeError).Message;
            return false;
        }

        _registered = true;
        RegisteredHotkey = definition.CanonicalText;
        error = string.Empty;
        return true;
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_messageSource.Handle, HotkeyId);
        _registered = false;
        RegisteredHotkey = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Unregister();
        _messageSource.RemoveHook(WindowProc);
        _messageSource.Dispose();
        _disposed = true;
    }

    private IntPtr WindowProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmHotKey && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
