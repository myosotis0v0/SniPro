using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SniPro.Core;
using SniPro.Windows;
using WpfButton = System.Windows.Controls.Button;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
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
    private const double HandleSize = 10;
    private const double OverlayControlEdgeMargin = 24;
    private const double OverlayControlGap = 8;
    private const double MaxHintWidth = 720;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly PixelRect _virtualBounds;
    private readonly PixelRect _initialMonitorBounds;
    private CaptureRegion? _selectedRegion;
    private CaptureRegion? _resizeBaseRegion;
    private (int X, int Y)? _dragStart;
    private ResizeHandle _activeResizeHandle;
    private bool _isDragging;
    private bool _isRecording;
    private bool _stopRequested;
    private bool _selectionControlsVisible;

    public CaptureOverlayWindow(LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        _virtualBounds = ScreenGeometry.GetVirtualScreenBounds();
        var cursor = GetCursorPosition();
        _initialMonitorBounds = ScreenGeometry.GetMonitorBounds(cursor.X, cursor.Y);

        InitializeComponent();
        SourceInitialized += CaptureOverlayWindow_SourceInitialized;
        Loaded += CaptureOverlayWindow_Loaded;
        SizeChanged += CaptureOverlayWindow_SizeChanged;
        Closed += CaptureOverlayWindow_Closed;
    }

    public event EventHandler<CaptureRegionEventArgs>? RecordingRequested;

    public event EventHandler? StopRecordingRequested;

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
        UpdateDimOverlay();
        UpdateOverlayControls();
        Activate();
        Focus();
    }

    private void CaptureOverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateDimOverlay();
        UpdateOverlayControls();
        Activate();
        Focus();
    }

    private void CaptureOverlayWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateDimOverlay();
        UpdateOverlayControls();
    }

    private void OverlayRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindVisualParent<WpfButton>(source) is not null)
        {
            return;
        }

        if (_isRecording)
        {
            e.Handled = true;
            return;
        }

        var point = GetCursorPosition();
        _dragStart = point;
        _resizeBaseRegion = null;
        _activeResizeHandle = ResizeHandle.None;
        _selectedRegion = null;
        _isDragging = true;
        HideSelectionVisuals();
        Mouse.Capture(OverlayRoot);
        e.Handled = true;
    }

    private void OverlayRoot_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isDragging || _isRecording)
        {
            return;
        }

        var point = GetCursorPosition();
        if (_activeResizeHandle == ResizeHandle.None)
        {
            UpdateNewSelection(point);
        }
        else
        {
            UpdateResizedSelection(point);
        }
    }

    private void OverlayRoot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging || _isRecording)
        {
            return;
        }

        var point = GetCursorPosition();
        if (_activeResizeHandle == ResizeHandle.None)
        {
            UpdateNewSelection(point);
        }
        else
        {
            UpdateResizedSelection(point);
        }

        _isDragging = false;
        _activeResizeHandle = ResizeHandle.None;
        _resizeBaseRegion = null;
        Mouse.Capture(null);
        ShowSelectionControls();
        e.Handled = true;
    }

    private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isRecording ||
            _selectedRegion is not { } region ||
            sender is not FrameworkElement element ||
            element.Tag is not string tag ||
            !Enum.TryParse(tag, out ResizeHandle resizeHandle))
        {
            return;
        }

        _resizeBaseRegion = region;
        _activeResizeHandle = resizeHandle;
        _isDragging = true;
        Mouse.Capture(OverlayRoot);
        e.Handled = true;
    }

    private void StartRecordingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }

        e.Handled = true;
    }

    private void CaptureOverlayWindow_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_isRecording)
            {
                StopRecording();
            }
            else
            {
                Cancel();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (_isRecording)
            {
                StopRecording();
            }
            else
            {
                StartRecording();
            }

            e.Handled = true;
        }
    }

    private void UpdateNewSelection((int X, int Y) current)
    {
        if (_dragStart is not { } start ||
            !CaptureRegion.TryCreate(start.X, start.Y, current.X, current.Y, out var region))
        {
            return;
        }

        ApplySelection(region);
    }

    private void UpdateResizedSelection((int X, int Y) current)
    {
        if (_resizeBaseRegion is not { } baseRegion ||
            !TryCreateResizedRegion(baseRegion, current, out var region))
        {
            return;
        }

        ApplySelection(region);
    }

    private bool TryCreateResizedRegion(
        CaptureRegion baseRegion,
        (int X, int Y) current,
        out CaptureRegion region)
    {
        return _activeResizeHandle switch
        {
            ResizeHandle.TopLeft => CaptureRegion.TryCreate(
                current.X,
                current.Y,
                baseRegion.Right,
                baseRegion.Bottom,
                out region),
            ResizeHandle.TopRight => CaptureRegion.TryCreate(
                baseRegion.X,
                current.Y,
                current.X,
                baseRegion.Bottom,
                out region),
            ResizeHandle.BottomLeft => CaptureRegion.TryCreate(
                current.X,
                baseRegion.Y,
                baseRegion.Right,
                current.Y,
                out region),
            ResizeHandle.BottomRight => CaptureRegion.TryCreate(
                baseRegion.X,
                baseRegion.Y,
                current.X,
                current.Y,
                out region),
            _ => InvalidRegion(out region)
        };
    }

    private static bool InvalidRegion(out CaptureRegion region)
    {
        region = default;
        return false;
    }

    private void ApplySelection(CaptureRegion region)
    {
        _selectedRegion = region;
        var topLeft = PointFromScreen(new WpfPoint(region.X, region.Y));
        var bottomRight = PointFromScreen(new WpfPoint(region.Right, region.Bottom));
        var selectionWidth = Math.Max(1, bottomRight.X - topLeft.X);
        var selectionHeight = Math.Max(1, bottomRight.Y - topLeft.Y);

        SelectionRectangle.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionRectangle, topLeft.X);
        Canvas.SetTop(SelectionRectangle, topLeft.Y);
        SelectionRectangle.Width = selectionWidth;
        SelectionRectangle.Height = selectionHeight;

        SelectionInfoText.Text = $"{region.Width} × {region.Height}";
        if (_selectionControlsVisible)
        {
            UpdateSelectionControls(topLeft, selectionWidth, selectionHeight);
        }
        else
        {
            SelectionInfoBorder.Visibility = Visibility.Collapsed;
            SetHandlesVisibility(Visibility.Collapsed);
        }

        UpdateDimOverlay();
    }

    private void ShowSelectionControls()
    {
        if (_selectedRegion is not { IsValid: true } region)
        {
            return;
        }

        _selectionControlsVisible = true;
        var topLeft = PointFromScreen(new WpfPoint(region.X, region.Y));
        var bottomRight = PointFromScreen(new WpfPoint(region.Right, region.Bottom));
        var selectionWidth = Math.Max(1, bottomRight.X - topLeft.X);
        var selectionHeight = Math.Max(1, bottomRight.Y - topLeft.Y);
        UpdateSelectionControls(topLeft, selectionWidth, selectionHeight);
        StartRecordingButton.Visibility = Visibility.Visible;
        UpdateOverlayControls();
    }

    private void UpdateSelectionControls(WpfPoint topLeft, double selectionWidth, double selectionHeight)
    {
        SelectionInfoBorder.Visibility = Visibility.Visible;
        SelectionInfoBorder.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        var infoWidth = SelectionInfoBorder.DesiredSize.Width;
        var infoHeight = SelectionInfoBorder.DesiredSize.Height;
        var infoLeft = Math.Clamp(topLeft.X, 0, Math.Max(0, OverlayRoot.ActualWidth - infoWidth));
        var infoTop = Math.Max(0, topLeft.Y - infoHeight - 4);
        Canvas.SetLeft(SelectionInfoBorder, infoLeft);
        Canvas.SetTop(SelectionInfoBorder, infoTop);
        SelectionInfoBorder.Width = infoWidth;
        SelectionInfoBorder.Height = infoHeight;

        SetHandlePosition(TopLeftHandle, topLeft.X, topLeft.Y);
        SetHandlePosition(TopRightHandle, topLeft.X + selectionWidth, topLeft.Y);
        SetHandlePosition(BottomLeftHandle, topLeft.X, topLeft.Y + selectionHeight);
        SetHandlePosition(BottomRightHandle, topLeft.X + selectionWidth, topLeft.Y + selectionHeight);
        SetHandlesVisibility(Visibility.Visible);
    }

    private void StartRecording()
    {
        if (_isRecording || _selectedRegion is not { } region || !region.IsValid)
        {
            return;
        }

        _isRecording = true;
        _stopRequested = false;
        SelectionRectangle.Visibility = Visibility.Collapsed;
        SelectionInfoBorder.Visibility = Visibility.Collapsed;
        SetHandlesVisibility(Visibility.Collapsed);
        HintText.SetResourceReference(TextBlock.TextProperty, "CaptureOverlayRecordingHint");
        StartRecordingButton.SetResourceReference(
            ContentControl.ContentProperty,
            "StopRecordingButton");
        StartRecordingButton.IsEnabled = true;
        UpdateDimOverlay();
        UpdateOverlayControls();
        RecordingRequested?.Invoke(this, new CaptureRegionEventArgs(region));
    }

    private void StopRecording()
    {
        if (!_isRecording || _stopRequested)
        {
            return;
        }

        _stopRequested = true;
        StartRecordingButton.IsEnabled = false;
        StopRecordingRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel()
    {
        if (_isRecording)
        {
            StopRecording();
            return;
        }

        Cancelled?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void HideSelectionVisuals()
    {
        SelectionRectangle.Visibility = Visibility.Collapsed;
        SelectionInfoBorder.Visibility = Visibility.Collapsed;
        StartRecordingButton.Visibility = Visibility.Collapsed;
        StartRecordingButton.IsEnabled = true;
        _selectionControlsVisible = false;
        HintText.SetResourceReference(TextBlock.TextProperty, "CaptureOverlayHint");
        StartRecordingButton.SetResourceReference(
            ContentControl.ContentProperty,
            "StartRecordingButton");
        SetHandlesVisibility(Visibility.Collapsed);
        UpdateDimOverlay();
        UpdateOverlayControls();
    }

    private void SetHandlesVisibility(Visibility visibility)
    {
        TopLeftHandle.Visibility = visibility;
        TopRightHandle.Visibility = visibility;
        BottomLeftHandle.Visibility = visibility;
        BottomRightHandle.Visibility = visibility;
    }

    private static void SetHandlePosition(FrameworkElement handle, double x, double y)
    {
        Canvas.SetLeft(handle, x - HandleSize / 2);
        Canvas.SetTop(handle, y - HandleSize / 2);
    }

    private void UpdateDimOverlay()
    {
        var width = OverlayRoot.ActualWidth;
        var height = OverlayRoot.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var outer = new RectangleGeometry(new WpfRect(0, 0, width, height));
        if (_selectedRegion is not { } region)
        {
            DimOverlayPath.Data = outer;
            return;
        }

        var topLeft = PointFromScreen(new WpfPoint(region.X, region.Y));
        var bottomRight = PointFromScreen(new WpfPoint(region.Right, region.Bottom));
        var selection = new WpfRect(
            topLeft.X,
            topLeft.Y,
            Math.Max(1, bottomRight.X - topLeft.X),
            Math.Max(1, bottomRight.Y - topLeft.Y));
        DimOverlayPath.Data = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            outer,
            new RectangleGeometry(selection));
    }

    private void UpdateOverlayControls()
    {
        var width = OverlayRoot.ActualWidth;
        var height = OverlayRoot.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var availableHintWidth = Math.Max(1, width - OverlayControlEdgeMargin * 2);
        var hintSize = MeasureHint(Math.Min(MaxHintWidth, availableHintWidth));

        if (!_selectionControlsVisible || !TryGetSelectionLayout(out var selection))
        {
            StartRecordingButton.Visibility = Visibility.Collapsed;
            var monitorTopLeft = PointFromScreen(
                new WpfPoint(_initialMonitorBounds.X, _initialMonitorBounds.Y));
            var monitorBottomRight = PointFromScreen(
                new WpfPoint(_initialMonitorBounds.Right, _initialMonitorBounds.Bottom));
            var monitorWidth = Math.Max(1, monitorBottomRight.X - monitorTopLeft.X);
            var monitorHeight = Math.Max(1, monitorBottomRight.Y - monitorTopLeft.Y);
            var initialHintLeft = monitorTopLeft.X + (monitorWidth - hintSize.Width) / 2;
            var initialHintTop = monitorTopLeft.Y + (monitorHeight - hintSize.Height) / 2;
            initialHintLeft = ClampToOverlay(initialHintLeft, hintSize.Width, width);
            initialHintTop = ClampToOverlay(initialHintTop, hintSize.Height, height);
            SetOverlayPosition(HintText, initialHintLeft, initialHintTop);
            return;
        }

        var topLeft = new WpfPoint(selection.Left, selection.Top);
        var bottomRight = new WpfPoint(selection.Right, selection.Bottom);
        var selectionWidth = selection.Width;

        var hintLeft = ClampToOverlay(
            topLeft.X + (selectionWidth - hintSize.Width) / 2,
            hintSize.Width,
            width);
        var hintTop = topLeft.Y - hintSize.Height - OverlayControlGap;
        if (hintTop < OverlayControlEdgeMargin)
        {
            var belowSelectionTop = bottomRight.Y + OverlayControlGap;
            hintTop = belowSelectionTop + hintSize.Height <= height - OverlayControlEdgeMargin
                ? belowSelectionTop
                : OverlayControlEdgeMargin;
        }

        SetOverlayPosition(HintText, hintLeft, hintTop);

        if (StartRecordingButton.Visibility != Visibility.Visible)
        {
            return;
        }

        StartRecordingButton.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        var buttonWidth = Math.Max(1, StartRecordingButton.DesiredSize.Width);
        var buttonHeight = Math.Max(1, StartRecordingButton.DesiredSize.Height);
        var buttonLeft = bottomRight.X + OverlayControlGap;
        var buttonTop = bottomRight.Y + OverlayControlGap;

        if (buttonLeft + buttonWidth > width - OverlayControlEdgeMargin)
        {
            buttonLeft = bottomRight.X - buttonWidth;
        }

        if (buttonTop + buttonHeight > height - OverlayControlEdgeMargin)
        {
            buttonTop = bottomRight.Y - buttonHeight;
        }

        buttonLeft = ClampToOverlay(buttonLeft, buttonWidth, width);
        buttonTop = ClampToOverlay(buttonTop, buttonHeight, height);
        SetOverlayPosition(StartRecordingButton, buttonLeft, buttonTop);
    }

    private bool TryGetSelectionLayout(out WpfRect selection)
    {
        var left = Canvas.GetLeft(SelectionRectangle);
        var top = Canvas.GetTop(SelectionRectangle);
        if (double.IsNaN(left) || double.IsNaN(top) ||
            SelectionRectangle.Width <= 0 || SelectionRectangle.Height <= 0)
        {
            selection = default;
            return false;
        }

        selection = new WpfRect(
            left,
            top,
            SelectionRectangle.Width,
            SelectionRectangle.Height);
        return true;
    }

    private WpfSize MeasureHint(double maxWidth)
    {
        HintText.Width = double.NaN;
        HintText.Height = double.NaN;
        HintText.MaxWidth = maxWidth;
        HintText.Measure(new WpfSize(maxWidth, double.PositiveInfinity));

        var width = Math.Min(maxWidth, Math.Max(1, HintText.DesiredSize.Width));
        HintText.Width = width;
        HintText.Measure(new WpfSize(width, double.PositiveInfinity));
        return new WpfSize(width, Math.Max(1, HintText.DesiredSize.Height));
    }

    private static void SetOverlayPosition(FrameworkElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
    }

    private static double ClampToOverlay(double value, double elementSize, double overlaySize)
    {
        var max = Math.Max(OverlayControlEdgeMargin, overlaySize - OverlayControlEdgeMargin - elementSize);
        return Math.Clamp(value, OverlayControlEdgeMargin, max);
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
        Loaded -= CaptureOverlayWindow_Loaded;
        SizeChanged -= CaptureOverlayWindow_SizeChanged;
        Closed -= CaptureOverlayWindow_Closed;
    }

    private static T? FindVisualParent<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private enum ResizeHandle
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
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
