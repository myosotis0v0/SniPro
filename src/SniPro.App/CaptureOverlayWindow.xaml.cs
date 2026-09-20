using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using SniPro.Core;
using SniPro.Windows;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace SniPro.App;

public sealed class CaptureRegionEventArgs : EventArgs
{
    public CaptureRegionEventArgs(CaptureRegion region)
    {
        Region = region;
    }

    public CaptureRegion Region { get; }
}

public partial class CaptureOverlayWindow : Window
{
    private const int SwpNoActivate = 0x0010;
    private const int SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly PixelRect _virtualBounds;
    private CaptureRegion? _selectedRegion;
    private (int X, int Y)? _dragStart;
    private bool _isDragging;

    public CaptureOverlayWindow(LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        _virtualBounds = ScreenGeometry.GetVirtualScreenBounds();

        InitializeComponent();
        SourceInitialized += CaptureOverlayWindow_SourceInitialized;
        Closed += CaptureOverlayWindow_Closed;
    }

    public event EventHandler<CaptureRegionEventArgs>? RegionConfirmed;

    public event EventHandler? Cancelled;

    private void CaptureOverlayWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(
            handle,
            HwndTopmost,
            _virtualBounds.X,
            _virtualBounds.Y,
            _virtualBounds.Width,
            _virtualBounds.Height,
            SwpNoActivate | SwpShowWindow);
        Activate();
        Focus();
    }

    private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var point = GetCursorPosition();
        _dragStart = point;
        _selectedRegion = null;
        _isDragging = true;
        SelectionRectangle.Visibility = Visibility.Visible;
        SelectionInfoBorder.Visibility = Visibility.Visible;
        Mouse.Capture(OverlayRoot);
        UpdateSelection(point);
        e.Handled = true;
    }

    private void OverlayCanvas_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_isDragging)
        {
            UpdateSelection(GetCursorPosition());
        }
    }

    private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var point = GetCursorPosition();
        UpdateSelection(point);
        _isDragging = false;
        Mouse.Capture(null);
        e.Handled = true;
    }

    private void CaptureOverlayWindow_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Confirm();
            e.Handled = true;
        }
    }

    private void UpdateSelection((int X, int Y) current)
    {
        if (_dragStart is not { } start ||
            !CaptureRegion.TryCreate(start.X, start.Y, current.X, current.Y, out var region))
        {
            return;
        }

        _selectedRegion = region;
        var topLeft = PointFromScreen(new WpfPoint(region.X, region.Y));
        var bottomRight = PointFromScreen(new WpfPoint(region.Right, region.Bottom));
        Canvas.SetLeft(SelectionRectangle, topLeft.X);
        Canvas.SetTop(SelectionRectangle, topLeft.Y);
        SelectionRectangle.Width = Math.Max(1, bottomRight.X - topLeft.X);
        SelectionRectangle.Height = Math.Max(1, bottomRight.Y - topLeft.Y);

        SelectionInfoText.Text = $"{region.Width} × {region.Height}";
        SelectionInfoBorder.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        var infoWidth = SelectionInfoBorder.DesiredSize.Width;
        var infoHeight = SelectionInfoBorder.DesiredSize.Height;
        Canvas.SetLeft(SelectionInfoBorder, topLeft.X);
        Canvas.SetTop(SelectionInfoBorder, Math.Max(0, topLeft.Y - infoHeight - 4));
        SelectionInfoBorder.Width = infoWidth;
        SelectionInfoBorder.Height = infoHeight;
    }

    private void Confirm()
    {
        if (_selectedRegion is not { } region || !region.IsValid)
        {
            return;
        }

        RegionConfirmed?.Invoke(this, new CaptureRegionEventArgs(region));
        Close();
    }

    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private (int X, int Y) GetCursorPosition()
    {
        return NativeMethods.GetCursorPos(out var point)
            ? (point.X, point.Y)
            : ((int)Left, (int)Top);
    }

    private void CaptureOverlayWindow_Closed(object? sender, EventArgs e)
    {
        if (_isDragging)
        {
            Mouse.Capture(null);
            _isDragging = false;
        }

        SourceInitialized -= CaptureOverlayWindow_SourceInitialized;
        Closed -= CaptureOverlayWindow_Closed;
    }

    private readonly record struct NativePoint(int X, int Y);

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int width,
            int height,
            int flags);
    }
}
