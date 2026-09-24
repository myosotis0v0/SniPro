using System.ComponentModel;
using System.Collections.Specialized;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SniPro.Windows;
using DrawingBitmap = System.Drawing.Bitmap;
using WinForms = System.Windows.Forms;
using WpfBrush = System.Windows.Media.Brush;

namespace SniPro.App;

public partial class CapturePreviewWindow : Window
{
    private readonly IReadOnlyList<DrawingBitmap> _frames;
    private readonly IReadOnlyList<BitmapImage> _frameImages;
    private readonly LocalizationService _localization;
    private readonly DispatcherTimer _playTimer;
    private readonly int _frameRate;
    private readonly string _outputDirectory;
    private readonly int _maxColors;
    private readonly bool _enableDithering;
    private CancellationTokenSource? _saveCancellation;
    private bool _updatingSliders;
    private bool _isSaving;
    private TimelineDragTarget _timelineDragTarget;
    private double _timelineDragStartX;
    private double _timelineDragOffsetX;
    private bool _timelineMoved;
    private int _startFrame;
    private int _endFrame;
    private int _currentFrame;

    private enum TimelineDragTarget
    {
        None,
        Playhead,
        Start,
        End
    }

    public CapturePreviewWindow(
        IReadOnlyList<DrawingBitmap> frames,
        int frameRate,
        string outputDirectory,
        int maxColors,
        bool enableDithering,
        LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxColors, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxColors, 256);
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one frame is required.", nameof(frames));
        }

        _frames = frames;
        _localization = localization;
        _frameRate = Math.Max(1, frameRate);
        _outputDirectory = outputDirectory.Trim();
        _maxColors = maxColors;
        _enableDithering = enableDithering;
        _frameImages = frames.Select(CreateBitmapImage).ToArray();
        _startFrame = 0;
        _endFrame = _frameImages.Count - 1;
        _currentFrame = _startFrame;

        InitializeComponent();
        var thumbnailCount = Math.Min(8, _frameImages.Count);
        TimelineThumbnails.ItemsSource = Enumerable.Range(0, thumbnailCount)
            .Select(index => _frameImages[thumbnailCount == 1
                ? 0
                : (int)Math.Round(index * (_frameImages.Count - 1d) / (thumbnailCount - 1))])
            .ToArray();
        _playTimer = new DispatcherTimer(
            DispatcherPriority.Render,
            Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1d / _frameRate)
        };
        _playTimer.Tick += PlayTimer_Tick;

        ApplySliderState(startFrame: 0, endFrame: _endFrame, currentFrame: 0);
        _playTimer.Start();
    }

    public (int StartFrame, int EndFrame) SelectedFrameRange => (_startFrame, _endFrame);

    private void StartFrameSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSliders)
        {
            return;
        }

        var startFrame = Math.Clamp((int)Math.Round(e.NewValue), 0, _endFrame);
        ApplySliderState(
            startFrame,
            _endFrame,
            Math.Max(_currentFrame, startFrame));
    }

    private void EndFrameSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSliders)
        {
            return;
        }

        var endFrame = Math.Clamp((int)Math.Round(e.NewValue), _startFrame, _frameImages.Count - 1);
        ApplySliderState(
            _startFrame,
            endFrame,
            Math.Min(_currentFrame, endFrame));
    }

    private void CurrentFrameSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSliders)
        {
            return;
        }

        var currentFrame = Math.Clamp((int)Math.Round(e.NewValue), _startFrame, _endFrame);
        ApplySliderState(_startFrame, _endFrame, currentFrame);
    }

    private void TimelineStrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var width = TimelineStrip.ActualWidth;
        if (_isSaving || width <= 0)
        {
            return;
        }

        var position = e.GetPosition(TimelineStrip);
        var startX = TimelineBoundaryX(_startFrame);
        var endX = TimelineBoundaryX(_endFrame + 1);
        var playheadX = TimelinePlayheadX();
        const double hitRadius = 12;
        var startDistance = Math.Abs(position.X - startX);
        var endDistance = Math.Abs(position.X - endX);
        var nearPlayhead = Math.Abs(position.X - playheadX) <= hitRadius;

        if (position.Y <= 16 && nearPlayhead)
        {
            _timelineDragTarget = TimelineDragTarget.Playhead;
        }
        else if (startDistance <= hitRadius || endDistance <= hitRadius)
        {
            _timelineDragTarget = startDistance <= endDistance
                ? TimelineDragTarget.Start
                : TimelineDragTarget.End;
        }
        else
        {
            _timelineDragTarget = TimelineDragTarget.Playhead;
        }

        _timelineDragStartX = position.X;
        _timelineMoved = false;
        _timelineDragOffsetX = _timelineDragTarget switch
        {
            TimelineDragTarget.Start => position.X - startX,
            TimelineDragTarget.End => position.X - endX,
            _ when nearPlayhead => position.X - playheadX,
            _ => 0
        };
        _playTimer.Stop();
        PlayButton.Content = _localization.Get("PreviewPlay");
        if (_timelineDragTarget == TimelineDragTarget.Playhead && !nearPlayhead)
        {
            UpdateTimelineFromPointer(position.X);
        }

        if (!TimelineStrip.CaptureMouse())
        {
            EndTimelineDrag();
        }
        e.Handled = true;
    }

    private void TimelineStrip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_timelineDragTarget == TimelineDragTarget.None || !TimelineStrip.IsMouseCaptured)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndTimelineDrag();
            return;
        }

        var x = e.GetPosition(TimelineStrip).X;
        if (!_timelineMoved && Math.Abs(x - _timelineDragStartX) < 2)
        {
            return;
        }

        _timelineMoved = true;
        UpdateTimelineFromPointer(x);
        e.Handled = true;
    }

    private void TimelineStrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_timelineDragTarget == TimelineDragTarget.None)
        {
            return;
        }

        if (_timelineMoved)
        {
            UpdateTimelineFromPointer(e.GetPosition(TimelineStrip).X);
        }

        EndTimelineDrag();
        e.Handled = true;
    }

    private void TimelineStrip_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _timelineDragTarget = TimelineDragTarget.None;
        _timelineMoved = false;
    }

    private void EndTimelineDrag()
    {
        _timelineDragTarget = TimelineDragTarget.None;
        _timelineMoved = false;
        if (TimelineStrip.IsMouseCaptured)
        {
            TimelineStrip.ReleaseMouseCapture();
        }
    }

    private void UpdateTimelineFromPointer(double pointerX)
    {
        var width = TimelineStrip.ActualWidth;
        if (_isSaving || width <= 0)
        {
            return;
        }

        var x = Math.Clamp(pointerX - _timelineDragOffsetX, 0, width);
        var boundary = (int)Math.Round(
            x * _frameImages.Count / width,
            MidpointRounding.AwayFromZero);
        switch (_timelineDragTarget)
        {
            case TimelineDragTarget.Start:
                var startFrame = Math.Clamp(boundary, 0, _endFrame);
                ApplySliderState(startFrame, _endFrame, Math.Max(_currentFrame, startFrame));
                break;
            case TimelineDragTarget.End:
                var endFrame = Math.Clamp(boundary - 1, _startFrame, _frameImages.Count - 1);
                ApplySliderState(_startFrame, endFrame, Math.Min(_currentFrame, endFrame));
                break;
            case TimelineDragTarget.Playhead:
                var frame = Math.Min(_frameImages.Count - 1, (int)(x * _frameImages.Count / width));
                ApplySliderState(_startFrame, _endFrame, Math.Clamp(frame, _startFrame, _endFrame));
                break;
        }
    }

    private double TimelineBoundaryX(int boundary) =>
        TimelineStrip.ActualWidth * boundary / _frameImages.Count;

    private double TimelinePlayheadX() =>
        TimelineStrip.ActualWidth * (_currentFrame + 0.5) / _frameImages.Count;

    private void TimelineOverlay_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTimelineVisuals();
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_playTimer.IsEnabled)
        {
            _playTimer.Stop();
            PlayButton.Content = _localization.Get("PreviewPlay");
        }
        else
        {
            if (_currentFrame >= _endFrame)
            {
                ApplySliderState(_startFrame, _endFrame, _startFrame);
            }

            _playTimer.Start();
            PlayButton.Content = _localization.Get("PreviewPause");
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void SaveGifButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isSaving)
        {
            return;
        }

        var startFrame = _startFrame;
        var endFrame = _endFrame;
        using var cancellation = new CancellationTokenSource();
        _saveCancellation = cancellation;
        _isSaving = true;
        EndTimelineDrag();
        _playTimer.Stop();
        PlayButton.Content = _localization.Get("PreviewPlay");
        TimelinePanel.IsEnabled = false;
        SaveGifButton.IsEnabled = false;
        PlayButton.IsEnabled = false;
        CloseButton.IsEnabled = false;
        SaveProgressBar.Visibility = Visibility.Visible;
        SaveProgressBar.IsIndeterminate = true;
        SaveProgressBar.Value = 0;
        SaveStatusText.Foreground = ThemeBrush("AccentBrush");
        SaveStatusText.Text = _localization.Get("PreviewSaving");
        UiMotion.FadeIn(SaveStatusText);

        try
        {
            var filePath = CreateGifPath();
            var progress = new Progress<GifEncodingProgress>(update =>
            {
                if (_isSaving && ReferenceEquals(_saveCancellation, cancellation))
                {
                    UpdateSaveProgress(update);
                }
            });
            await GifEncoder.SaveAsync(
                _frames,
                _frameRate,
                startFrame,
                endFrame,
                filePath,
                cancellationToken: cancellation.Token,
                maxColors: _maxColors,
                enableDithering: _enableDithering,
                progress: progress);

            if (TryCopyGifToClipboard(filePath))
            {
                SaveStatusText.Foreground = ThemeBrush("SuccessBrush");
                SaveStatusText.Text = _localization.Format("PreviewSaved", filePath);
            }
            else
            {
                SaveStatusText.Foreground = ThemeBrush("WarningBrush");
                SaveStatusText.Text = _localization.Format(
                    "PreviewSavedClipboardFailed",
                    filePath);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SaveStatusText.Foreground = ThemeBrush("MutedBrush");
            SaveStatusText.Text = _localization.Get("PreviewSaveCancelled");
        }
        catch (Exception exception)
        {
            SaveStatusText.Foreground = ThemeBrush("ErrorBrush");
            SaveStatusText.Text = _localization.Format(
                "PreviewSaveFailed",
                exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_saveCancellation, cancellation))
            {
                _saveCancellation = null;
            }

            _isSaving = false;
            TimelinePanel.IsEnabled = true;
            SaveProgressBar.Visibility = Visibility.Collapsed;
            SaveGifButton.IsEnabled = true;
            PlayButton.IsEnabled = true;
            CloseButton.IsEnabled = true;
            UiMotion.FadeIn(SaveStatusText);
        }
    }

    private void UpdateSaveProgress(GifEncodingProgress update)
    {
        SaveStatusText.Text = update.Stage switch
        {
            GifEncodingStage.AnalyzingColors => _localization.Get("PreviewAnalyzingColors"),
            GifEncodingStage.PreparingPalette => _localization.Get("PreviewPreparingPalette"),
            GifEncodingStage.EncodingFrames => _localization.Format(
                "PreviewEncodingFrames",
                update.CompletedFrames,
                update.TotalFrames),
            GifEncodingStage.Finalizing => _localization.Get("PreviewFinalizing"),
            _ => _localization.Get("PreviewSaving")
        };

        SaveProgressBar.IsIndeterminate = update.Stage != GifEncodingStage.EncodingFrames;
        if (update.Stage == GifEncodingStage.EncodingFrames)
        {
            SaveProgressBar.Maximum = Math.Max(1, update.TotalFrames);
            SaveProgressBar.Value = update.CompletedFrames;
        }
    }

    private string CreateGifPath()
    {
        var outputDirectory = Path.GetFullPath(_outputDirectory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var filePath = Path.Combine(outputDirectory, $"SniPro_{timestamp}.gif");
        var suffix = 2;

        while (File.Exists(filePath))
        {
            filePath = Path.Combine(
                outputDirectory,
                $"SniPro_{timestamp}_{suffix}.gif");
            suffix++;
        }

        return filePath;
    }

    private static bool TryCopyGifToClipboard(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var files = new StringCollection();
            files.Add(Path.GetFullPath(filePath));

            var data = new WinForms.DataObject();
            data.SetFileDropList(files);
            WinForms.Clipboard.SetDataObject(
                data,
                copy: true,
                retryTimes: 20,
                retryDelay: 100);
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void PlayTimer_Tick(object? sender, EventArgs e)
    {
        var nextFrame = _currentFrame >= _endFrame
            ? _startFrame
            : _currentFrame + 1;
        ApplySliderState(_startFrame, _endFrame, nextFrame);
    }

    private void ApplySliderState(int startFrame, int endFrame, int currentFrame)
    {
        _startFrame = Math.Clamp(startFrame, 0, _frameImages.Count - 1);
        _endFrame = Math.Clamp(endFrame, _startFrame, _frameImages.Count - 1);
        _currentFrame = Math.Clamp(currentFrame, _startFrame, _endFrame);

        _updatingSliders = true;
        StartFrameSlider.Maximum = _frameImages.Count - 1;
        EndFrameSlider.Maximum = _frameImages.Count - 1;
        CurrentFrameSlider.Maximum = _frameImages.Count - 1;
        StartFrameSlider.Value = _startFrame;
        EndFrameSlider.Value = _endFrame;
        CurrentFrameSlider.Minimum = _startFrame;
        CurrentFrameSlider.Maximum = _endFrame;
        CurrentFrameSlider.Value = _currentFrame;
        _updatingSliders = false;

        PreviewImage.Source = _frameImages[_currentFrame];
        FrameStatusText.Text = _localization.Format(
            "PreviewFrameStatus",
            _currentFrame + 1,
            _frameImages.Count,
            _startFrame + 1,
            _endFrame + 1);
        UpdateTimelineVisuals();
    }

    private void UpdateTimelineVisuals()
    {
        var width = TimelineOverlay.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var frameCount = _frameImages.Count;
        var selectedLeft = width * _startFrame / frameCount;
        var selectedRight = width * (_endFrame + 1d) / frameCount;
        TimelineLeftShade.Width = selectedLeft;
        Canvas.SetLeft(TimelineLeftShade, 0);
        TimelineRightShade.Width = Math.Max(0, width - selectedRight);
        Canvas.SetLeft(TimelineRightShade, selectedRight);
        TimelineRange.Width = Math.Max(2, selectedRight - selectedLeft);
        Canvas.SetLeft(TimelineRange, selectedLeft);
        Canvas.SetLeft(TimelineStartHandle, Math.Clamp(selectedLeft - 4.5, 0, Math.Max(0, width - 9)));
        Canvas.SetLeft(TimelineEndHandle, Math.Clamp(selectedRight - 4.5, 0, Math.Max(0, width - 9)));
        Canvas.SetLeft(
            TimelinePlayhead,
            Math.Clamp(width * (_currentFrame + 0.5) / frameCount - 1.5, 0, Math.Max(0, width - 3)));
        Canvas.SetLeft(
            TimelinePlayheadMarker,
            Math.Clamp(width * (_currentFrame + 0.5) / frameCount - 6, 0, Math.Max(0, width - 12)));
    }

    private static BitmapImage CreateBitmapImage(DrawingBitmap frame)
    {
        using var stream = new MemoryStream();
        frame.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void CapturePreviewWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isSaving)
        {
            return;
        }

        e.Cancel = true;
        _saveCancellation?.Cancel();
        SaveStatusText.Foreground = ThemeBrush("MutedBrush");
        SaveStatusText.Text = _localization.Get("PreviewSaveCancelling");
    }

    private WpfBrush ThemeBrush(string key) => (WpfBrush)FindResource(key);

    private void CapturePreviewWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UiMotion.Reveal(PreviewHeader);
        UiMotion.Reveal(PreviewStage, 45);
        UiMotion.Reveal(TimelinePanel, 100);
    }

    private void CapturePreviewWindow_Closed(object? sender, EventArgs e)
    {
        _saveCancellation?.Cancel();
        EndTimelineDrag();
        _playTimer.Stop();
        _playTimer.Tick -= PlayTimer_Tick;
        ScreenRecorder.DisposeFrames(_frames);
    }
}
