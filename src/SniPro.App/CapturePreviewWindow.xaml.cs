using System.ComponentModel;
using System.Collections.Specialized;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
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
    private int _startFrame;
    private int _endFrame;
    private int _currentFrame;

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
        _playTimer.Stop();
        _playTimer.Tick -= PlayTimer_Tick;
        ScreenRecorder.DisposeFrames(_frames);
    }
}
