using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SniPro.Core;
using SniPro.Windows;
using WpfButton = System.Windows.Controls.Button;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfCursors = System.Windows.Input.Cursors;
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
    private const double OverlayControlEdgeMargin = 24;
    private const double OverlayControlGap = 8;
    private const double RecordingOutlineMargin = 4;
    private const double MaxHintWidth = 720;
    private const int DragThresholdPixels = 4;
    private const int EdgeTolerancePixels = 8;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly PixelRect _virtualBounds;
    private readonly PixelRect _initialMonitorBounds;
    private CaptureRegion? _selectedRegion;
    private CaptureRegion? _dragBaseRegion;
    private (int X, int Y)? _dragStart;
    private CaptureRegionHit _dragHit;
    private bool _isDragging;
    private bool _dragMoved;
    private bool _isRecording;
    private bool _stopRequested;
    private bool _selectionControlsVisible;
    private bool _isForwardingClick;
    private bool _isClosed;
    private string? _measuredHintText;
    private double _measuredHintMaxWidth;
    private WpfSize _measuredHintSize;

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
        if (_isRecording && _selectedRegion is { } region)
        {
            UpdateRecordingOutline(region);
        }
    }

    private void OverlayRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindVisualParent<WpfButton>(source) is not null)
        {
            return;
        }

        if (_isForwardingClick)
        {
            e.Handled = true;
            return;
        }

        var point = GetCursorPosition();
        _dragStart = point;
        _dragBaseRegion = _selectedRegion;
        _dragHit = _isRecording
            ? CaptureRegionHit.Outside
            : _selectedRegion is { } region
                ? CaptureRegionEditor.HitTest(region, point.X, point.Y, EdgeTolerancePixels)
                : CaptureRegionHit.Inside;
        _isDragging = true;
        _dragMoved = false;
        if (!Mouse.Capture(OverlayRoot))
        {
            ResetDrag();
        }
        e.Handled = true;
    }

    private void OverlayRoot_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isDragging)
        {
            UpdateSelectionCursor();
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetDrag();
            return;
        }

        var point = GetCursorPosition();
        if (!_dragMoved && !MovedBeyondThreshold(point))
        {
            return;
        }

        _dragMoved = true;
        UpdateDraggedSelection(point);
        e.Handled = true;
    }

    private void OverlayRoot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var point = GetCursorPosition();
        var wasDrag = _dragMoved || MovedBeyondThreshold(point);
        if (wasDrag)
        {
            UpdateDraggedSelection(point);
        }

        ResetDrag();
        if (!wasDrag)
        {
            ForwardClickToUnderlyingApplication(point);
        }

        e.Handled = true;
    }

    private void OverlayRoot_LostMouseCapture(object sender, WpfMouseEventArgs e)
    {
        _isDragging = false;
        _dragMoved = false;
        _dragStart = null;
        _dragBaseRegion = null;
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

    private bool MovedBeyondThreshold((int X, int Y) current)
    {
        return _dragStart is { } start &&
            (Math.Abs(current.X - start.X) >= DragThresholdPixels ||
             Math.Abs(current.Y - start.Y) >= DragThresholdPixels);
    }

    private void UpdateDraggedSelection((int X, int Y) current)
    {
        if (_dragStart is not { } start || _dragHit == CaptureRegionHit.Outside)
        {
            return;
        }

        var x = Math.Clamp(current.X, _virtualBounds.X, _virtualBounds.Right);
        var y = Math.Clamp(current.Y, _virtualBounds.Y, _virtualBounds.Bottom);
        if (_dragBaseRegion is not { } baseRegion)
        {
            if (CaptureRegion.TryCreate(start.X, start.Y, x, y, out var newRegion))
            {
                ApplySelection(newRegion);
            }
            return;
        }

        var deltaX = x - start.X;
        var deltaY = y - start.Y;
        var region = _dragHit == CaptureRegionHit.Inside
            ? CaptureRegionEditor.Move(baseRegion, deltaX, deltaY, _virtualBounds)
            : CaptureRegionEditor.Resize(baseRegion, _dragHit, deltaX, deltaY, _virtualBounds);
        ApplySelection(region);
    }

    private void ResetDrag()
    {
        _isDragging = false;
        _dragMoved = false;
        _dragStart = null;
        _dragBaseRegion = null;
        _dragHit = CaptureRegionHit.Outside;
        if (ReferenceEquals(Mouse.Captured, OverlayRoot))
        {
            Mouse.Capture(null);
        }
    }

    private void UpdateSelectionCursor()
    {
        if (_isRecording)
        {
            OverlayRoot.Cursor = WpfCursors.Arrow;
            return;
        }

        if (_selectedRegion is not { } region)
        {
            OverlayRoot.Cursor = WpfCursors.Cross;
            return;
        }

        var point = GetCursorPosition();
        OverlayRoot.Cursor = CaptureRegionEditor.HitTest(
            region, point.X, point.Y, EdgeTolerancePixels) switch
        {
            CaptureRegionHit.TopLeft or CaptureRegionHit.BottomRight => WpfCursors.SizeNWSE,
            CaptureRegionHit.TopRight or CaptureRegionHit.BottomLeft => WpfCursors.SizeNESW,
            CaptureRegionHit.Left or CaptureRegionHit.Right => WpfCursors.SizeWE,
            CaptureRegionHit.Top or CaptureRegionHit.Bottom => WpfCursors.SizeNS,
            CaptureRegionHit.Inside => WpfCursors.SizeAll,
            _ => WpfCursors.Arrow
        };
    }

    private async void ForwardClickToUnderlyingApplication((int X, int Y) point)
    {
        if (_isClosed || _isForwardingClick)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        Marshal.SetLastPInvokeError(0);
        var originalStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExStyle);
        if (originalStyle == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            return;
        }

        _isForwardingClick = true;
        try
        {
            // The layered overlay consumed the physical click; replay it while native hit testing skips this window.
            if (!NativeMethods.SetExtendedStyle(handle, originalStyle | NativeMethods.WsExTransparent))
            {
                return;
            }

            NativeMethods.SetCursorPos(point.X, point.Y);
            NativeMethods.SendLeftClick();
            await Task.Delay(120);
        }
        finally
        {
            if (!_isClosed)
            {
                NativeMethods.SetExtendedStyle(handle, originalStyle);
            }
            _isForwardingClick = false;
        }
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
            ShowSelectionControls();
        }

        UpdateDimOverlay();
        UpdateOverlayControls();
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
        UiMotion.Reveal(HintBorder);
        UiMotion.Reveal(StartRecordingButton);
    }

    private void UpdateSelectionControls(WpfPoint topLeft, double selectionWidth, double selectionHeight)
    {
        SelectionInfoBorder.Visibility = Visibility.Visible;
        SelectionInfoBorder.Width = double.NaN;
        SelectionInfoBorder.Height = double.NaN;
        SelectionInfoText.InvalidateMeasure();
        SelectionInfoBorder.InvalidateMeasure();
        SelectionInfoBorder.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        var infoWidth = SelectionInfoBorder.DesiredSize.Width;
        var infoHeight = SelectionInfoBorder.DesiredSize.Height;
        var infoLeft = Math.Clamp(topLeft.X, 0, Math.Max(0, OverlayRoot.ActualWidth - infoWidth));
        var infoTop = Math.Max(0, topLeft.Y - infoHeight - 4);
        Canvas.SetLeft(SelectionInfoBorder, infoLeft);
        Canvas.SetTop(SelectionInfoBorder, infoTop);

        SetHandlePosition(TopLeftHandle, topLeft.X, topLeft.Y);
        SetHandlePosition(TopRightHandle, topLeft.X + selectionWidth, topLeft.Y);
        SetHandlePosition(BottomLeftHandle, topLeft.X, topLeft.Y + selectionHeight);
        SetHandlePosition(BottomRightHandle, topLeft.X + selectionWidth, topLeft.Y + selectionHeight);
        SetHandlePosition(TopHandle, topLeft.X + selectionWidth / 2, topLeft.Y);
        SetHandlePosition(RightHandle, topLeft.X + selectionWidth, topLeft.Y + selectionHeight / 2);
        SetHandlePosition(BottomHandle, topLeft.X + selectionWidth / 2, topLeft.Y + selectionHeight);
        SetHandlePosition(LeftHandle, topLeft.X, topLeft.Y + selectionHeight / 2);
        SetHandlesVisibility(Visibility.Visible);
    }

    private void StartRecording()
    {
        if (_isRecording || _selectedRegion is not { } region || !region.IsValid)
        {
            return;
        }

        ResetDrag();
        _isRecording = true;
        _stopRequested = false;
        SelectionRectangle.Visibility = Visibility.Collapsed;
        SelectionInfoBorder.Visibility = Visibility.Collapsed;
        SetHandlesVisibility(Visibility.Collapsed);
        UpdateRecordingOutline(region);
        RecordingOutline.Visibility = Visibility.Visible;
        if (SystemParameters.ClientAreaAnimation)
        {
            RecordingOutline.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(900))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                });
        }
        HintText.SetResourceReference(TextBlock.TextProperty, "CaptureOverlayRecordingHint");
        HintBorder.Padding = new Thickness(29, 10, 17, 10);
        RecordingDot.Visibility = Visibility.Visible;
        if (SystemParameters.ClientAreaAnimation)
        {
            RecordingDot.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(780))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                });
        }
        StartRecordingButton.SetResourceReference(
            ContentControl.ContentProperty,
            "StopRecordingButton");
        StartRecordingButton.IsEnabled = true;
        UpdateDimOverlay();
        UpdateOverlayControls();
        UiMotion.Reveal(HintBorder);
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
        HintText.SetResourceReference(TextBlock.TextProperty, "CaptureOverlayFinishingHint");
        RecordingDot.BeginAnimation(UIElement.OpacityProperty, null);
        RecordingDot.Opacity = 0.65;
        RecordingOutline.BeginAnimation(UIElement.OpacityProperty, null);
        RecordingOutline.Opacity = 0.6;
        UpdateOverlayControls();
        StopRecordingRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel()
    {
        if (_isRecording)
        {
            StopRecording();
            return;
        }

        Close();
    }

    private void SetHandlesVisibility(Visibility visibility)
    {
        TopLeftHandle.Visibility = visibility;
        TopRightHandle.Visibility = visibility;
        BottomLeftHandle.Visibility = visibility;
        BottomRightHandle.Visibility = visibility;
        TopHandle.Visibility = visibility;
        RightHandle.Visibility = visibility;
        BottomHandle.Visibility = visibility;
        LeftHandle.Visibility = visibility;
    }

    private static void SetHandlePosition(FrameworkElement handle, double x, double y)
    {
        Canvas.SetLeft(handle, x - handle.Width / 2);
        Canvas.SetTop(handle, y - handle.Height / 2);
    }

    private void UpdateRecordingOutline(CaptureRegion region)
    {
        var topLeft = PointFromScreen(new WpfPoint(region.X, region.Y));
        var bottomRight = PointFromScreen(new WpfPoint(region.Right, region.Bottom));
        // Keep the stroke beyond the sampled screen rectangle.
        Canvas.SetLeft(RecordingOutline, topLeft.X - RecordingOutlineMargin);
        Canvas.SetTop(RecordingOutline, topLeft.Y - RecordingOutlineMargin);
        RecordingOutline.Width = Math.Max(1, bottomRight.X - topLeft.X) + RecordingOutlineMargin * 2;
        RecordingOutline.Height = Math.Max(1, bottomRight.Y - topLeft.Y) + RecordingOutlineMargin * 2;
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
            SetOverlayPosition(HintBorder, initialHintLeft, initialHintTop);
            UpdateRecordingDot(initialHintLeft, initialHintTop, hintSize.Height);
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

        SetOverlayPosition(HintBorder, hintLeft, hintTop);
        UpdateRecordingDot(hintLeft, hintTop, hintSize.Height);

        if (StartRecordingButton.Visibility != Visibility.Visible)
        {
            return;
        }

        StartRecordingButton.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        var buttonWidth = Math.Max(1, StartRecordingButton.DesiredSize.Width);
        var buttonHeight = Math.Max(1, StartRecordingButton.DesiredSize.Height);
        var buttonPosition = GetRecordingButtonPosition(
            selection,
            new WpfSize(buttonWidth, buttonHeight),
            new WpfSize(width, height));
        var buttonLeft = ClampToOverlay(buttonPosition.X, buttonWidth, width);
        var buttonTop = ClampToOverlay(buttonPosition.Y, buttonHeight, height);
        SetOverlayPosition(StartRecordingButton, buttonLeft, buttonTop);
    }

    private static WpfPoint GetRecordingButtonPosition(
        WpfRect selection,
        WpfSize button,
        WpfSize overlay)
    {
        if (selection.Bottom + OverlayControlGap + button.Height <= overlay.Height - OverlayControlEdgeMargin)
        {
            return new WpfPoint(selection.Right - button.Width, selection.Bottom + OverlayControlGap);
        }

        if (selection.Right + OverlayControlGap + button.Width <= overlay.Width - OverlayControlEdgeMargin)
        {
            return new WpfPoint(selection.Right + OverlayControlGap, selection.Bottom - button.Height);
        }

        if (selection.Top - OverlayControlGap - button.Height >= OverlayControlEdgeMargin)
        {
            return new WpfPoint(selection.Right - button.Width, selection.Top - OverlayControlGap - button.Height);
        }

        if (selection.Left - OverlayControlGap - button.Width >= OverlayControlEdgeMargin)
        {
            return new WpfPoint(selection.Left - OverlayControlGap - button.Width, selection.Bottom - button.Height);
        }

        return new WpfPoint(selection.Right - button.Width, selection.Bottom - button.Height);
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

    private void UpdateRecordingDot(double hintLeft, double hintTop, double hintHeight)
    {
        if (RecordingDot.Visibility != Visibility.Visible)
        {
            return;
        }

        SetOverlayPosition(
            RecordingDot,
            hintLeft + 14,
            hintTop + Math.Max(0, (hintHeight - RecordingDot.Height) / 2));
    }

    private WpfSize MeasureHint(double maxWidth)
    {
        if (_measuredHintText == HintText.Text &&
            Math.Abs(_measuredHintMaxWidth - maxWidth) < 0.5)
        {
            return _measuredHintSize;
        }

        HintBorder.Width = double.NaN;
        HintBorder.Height = double.NaN;
        HintBorder.MaxWidth = maxWidth;
        HintBorder.Measure(new WpfSize(maxWidth, double.PositiveInfinity));

        var width = Math.Min(maxWidth, Math.Max(1, HintBorder.DesiredSize.Width));
        HintBorder.Width = width;
        HintBorder.Measure(new WpfSize(width, double.PositiveInfinity));
        _measuredHintText = HintText.Text;
        _measuredHintMaxWidth = maxWidth;
        _measuredHintSize = new WpfSize(width, Math.Max(1, HintBorder.DesiredSize.Height));
        return _measuredHintSize;
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
        _isClosed = true;
        RecordingDot.BeginAnimation(UIElement.OpacityProperty, null);
        RecordingOutline.BeginAnimation(UIElement.OpacityProperty, null);
        if (_isDragging)
        {
            ResetDrag();
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

    private readonly record struct NativePoint(int X, int Y);

    private static class NativeMethods
    {
        public const int GwlExStyle = -20;
        public const int WsExTransparent = 0x00000020;
        private const int SwpNoSize = 0x0001;
        private const int SwpNoMove = 0x0002;
        private const int SwpNoZOrder = 0x0004;
        private const int SwpFrameChanged = 0x0020;
        private const uint InputMouse = 0;
        private const uint MouseLeftDown = 0x0002;
        private const uint MouseLeftUp = 0x0004;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, [In] NativeInput[] inputs, int size);

        public static bool SetExtendedStyle(IntPtr handle, int style)
        {
            Marshal.SetLastPInvokeError(0);
            var previous = SetWindowLong(handle, GwlExStyle, style);
            if (previous == 0 && Marshal.GetLastPInvokeError() != 0)
            {
                return false;
            }

            return SetWindowPos(
                handle,
                IntPtr.Zero,
                0, 0, 0, 0,
                SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }

        public static bool SendLeftClick()
        {
            var inputs = new[]
            {
                new NativeInput { Type = InputMouse, Mouse = new NativeMouseInput { Flags = MouseLeftDown } },
                new NativeInput { Type = InputMouse, Mouse = new NativeMouseInput { Flags = MouseLeftUp } }
            };
            return SendInput(2, inputs, Marshal.SizeOf<NativeInput>()) == 2;
        }

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

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeInput
        {
            public uint Type;
            public NativeMouseInput Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMouseInput
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }
    }
}
