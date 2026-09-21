using System.ComponentModel;
using System.Threading;
using System.Windows;
using SniPro.Core;
using SniPro.Windows;
using WinForms = System.Windows.Forms;

namespace SniPro.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private MainWindow? _mainWindow;
    private WinForms.NotifyIcon? _notifyIcon;
    private System.Drawing.Icon? _trayIcon;
    private JsonSettingsStore? _settingsStore;
    private StartupManager? _startupManager;
    private GlobalHotkeyHost? _hotkeyHost;
    private CaptureOverlayWindow? _captureOverlay;
    private CapturePreviewWindow? _capturePreview;
    private CancellationTokenSource? _recordingCancellation;
    private LocalizationService? _localization;
    private WinForms.ToolStripMenuItem? _openTrayItem;
    private WinForms.ToolStripMenuItem? _exitTrayItem;
    private AppSettings _settings = AppSettingsDefaults.Create();
    private bool _isExiting;
    private bool _isRecording;
    private bool _restoreMainWindowAfterCapture;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        var mutex = new Mutex(true, SniProIdentity.SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            Shutdown(1);
            return;
        }

        _singleInstanceMutex = mutex;
        _settingsStore = new JsonSettingsStore(SettingsFilePath.GetDefault());
        _settings = _settingsStore.Load();
        _localization = new LocalizationService();
        _localization.LanguageChanged += OnLanguageChanged;
        _localization.Apply(_settings.LanguageCode);
        _startupManager = new StartupManager(
            SniProIdentity.Name,
            Environment.ProcessPath ?? throw new InvalidOperationException("The process path is unavailable."));
        _mainWindow = new MainWindow(_settings, SaveSettings, _localization);
        _mainWindow.Closing += OnMainWindowClosing;

        CreateTrayIcon();
        ApplyStartupSetting(_settings.StartWithWindows, notifyOnFailure: true);

        _hotkeyHost = new GlobalHotkeyHost();
        _hotkeyHost.HotkeyPressed += OnCaptureHotkeyPressed;
        if (!TryRegisterCaptureHotkey(_settings.CaptureHotkey, out var hotkeyError))
        {
            ShowTrayNotification(
                _localization.Get("TrayGlobalHotkeyUnavailable"),
                hotkeyError);
        }

        if (e.Args.Any(argument => string.Equals(argument, "--show", StringComparison.OrdinalIgnoreCase)))
        {
            ShowMainWindow();
        }
    }

    private void CreateTrayIcon()
    {
        var contextMenu = new WinForms.ContextMenuStrip();
        _openTrayItem = new WinForms.ToolStripMenuItem(_localization?.Get("TrayOpen") ?? "Open");
        _openTrayItem.Click += (_, _) => ShowMainWindow();

        _exitTrayItem = new WinForms.ToolStripMenuItem(_localization?.Get("TrayExit") ?? "Exit");
        _exitTrayItem.Click += (_, _) => ExitApplication();

        contextMenu.Items.Add(_openTrayItem);
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add(_exitTrayItem);

        _trayIcon = TryLoadApplicationIcon();
        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _trayIcon ?? System.Drawing.SystemIcons.Application,
            Text = SniProIdentity.Name,
            ContextMenuStrip = contextMenu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private static System.Drawing.Icon? TryLoadApplicationIcon()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            return string.IsNullOrWhiteSpace(processPath)
                ? null
                : System.Drawing.Icon.ExtractAssociatedIcon(processPath);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.LoadSettings(_settings);

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    private void SaveSettings(AppSettings settings)
    {
        if (_settingsStore is null || _startupManager is null || _hotkeyHost is null || _localization is null)
        {
            throw new InvalidOperationException("The application services are not initialized.");
        }

        var normalized = AppSettingsValidator.Normalize(settings);
        var previous = _settings.Clone();
        _settingsStore.Save(normalized);

        try
        {
            _localization.Apply(normalized.LanguageCode);
            _startupManager.Apply(normalized.StartWithWindows);
            if (!TryRegisterCaptureHotkey(normalized.CaptureHotkey, out var hotkeyError))
            {
                throw new InvalidOperationException($"Could not register the capture hotkey: {hotkeyError}");
            }

            _settings = normalized;
        }
        catch
        {
            TryRestoreSettings(previous);
            throw;
        }
    }

    private void TryRestoreSettings(AppSettings settings)
    {
        try
        {
            _settingsStore?.Save(settings);
        }
        catch
        {
            // Preserve the original save error for the settings window.
        }

        try
        {
            _localization?.Apply(settings.LanguageCode);
        }
        catch
        {
            // Preserve the original save error for the settings window.
        }

        try
        {
            _startupManager?.Apply(settings.StartWithWindows);
        }
        catch
        {
            // Preserve the original save error for the settings window.
        }

        try
        {
            _hotkeyHost?.TryRegister(settings.CaptureHotkey, out _);
        }
        catch
        {
            // Preserve the original save error for the settings window.
        }
    }

    private void ApplyStartupSetting(bool enabled, bool notifyOnFailure)
    {
        try
        {
            _startupManager?.Apply(enabled);
        }
        catch (Exception exception) when (notifyOnFailure)
        {
            ShowTrayNotification(
                _localization?.Get("TrayStartupUnavailable") ?? "Startup setting unavailable",
                exception.Message);
        }
    }

    private bool TryRegisterCaptureHotkey(string hotkey, out string error)
    {
        if (_hotkeyHost is null)
        {
            error = "The global hotkey service is not initialized.";
            return false;
        }

        return _hotkeyHost.TryRegister(hotkey, out error);
    }

    private void OnCaptureHotkeyPressed(object? sender, EventArgs e)
    {
        BeginCapture();
    }

    private void BeginCapture()
    {
        if (_captureOverlay is not null ||
            _capturePreview is not null ||
            _isRecording ||
            _localization is null)
        {
            return;
        }

        try
        {
            _captureOverlay = new CaptureOverlayWindow(_localization);
            _captureOverlay.RecordingRequested += OnCaptureRecordingRequested;
            _captureOverlay.StopRecordingRequested += OnCaptureStopRecordingRequested;
            _captureOverlay.Closed += OnCaptureOverlayClosed;
            _captureOverlay.Show();
        }
        catch (Exception exception)
        {
            _captureOverlay = null;
            ShowTrayNotification(
                _localization.Get("TrayGlobalHotkeyUnavailable"),
                exception.Message);
        }
    }

    private async void OnCaptureRecordingRequested(object? sender, CaptureRegionEventArgs e)
    {
        if (_isRecording || _localization is null)
        {
            return;
        }

        _isRecording = true;
        _restoreMainWindowAfterCapture = _mainWindow?.IsVisible == true;
        _mainWindow?.Hide();
        using var cancellation = new CancellationTokenSource();
        _recordingCancellation = cancellation;
        var settings = _settings.Clone();

        try
        {
            var frames = await ScreenRecorder.RecordAsync(
                e.Region,
                settings.FrameRate,
                settings.ScalePercent,
                cancellation.Token);

            if (sender is CaptureOverlayWindow overlay)
            {
                overlay.Close();
            }

            if (_isExiting)
            {
                ScreenRecorder.DisposeFrames(frames);
                return;
            }

            ShowCapturePreview(
                frames,
                settings.FrameRate,
                settings.OutputDirectory,
                settings.MaxColors,
                settings.EnableDithering);
        }
        catch (Exception exception)
        {
            if (sender is CaptureOverlayWindow overlay)
            {
                overlay.Close();
            }

            if (!_isExiting && _localization is not null)
            {
                var message = exception is RecordingBufferLimitException bufferLimit
                    ? _localization.Format(
                        "StatusRecordingBufferLimit",
                        bufferLimit.LimitMegabytes)
                    : _localization.Format("StatusRecordingFailed", exception.Message);
                ShowTrayNotification(
                    _localization.Get("TrayRecordingFailed"),
                    message);
                RestoreMainWindowAfterCapture();
            }
        }
        finally
        {
            if (ReferenceEquals(_recordingCancellation, cancellation))
            {
                _recordingCancellation = null;
            }

            _isRecording = false;
        }
    }

    private void OnCaptureStopRecordingRequested(object? sender, EventArgs e)
    {
        if (sender is CaptureOverlayWindow overlay && ReferenceEquals(_captureOverlay, overlay))
        {
            _recordingCancellation?.Cancel();
        }
    }

    private void ShowCapturePreview(
        IReadOnlyList<System.Drawing.Bitmap> frames,
        int frameRate,
        string outputDirectory,
        int maxColors,
        bool enableDithering)
    {
        if (_localization is null)
        {
            ScreenRecorder.DisposeFrames(frames);
            return;
        }

        try
        {
            _capturePreview = new CapturePreviewWindow(
                frames,
                frameRate,
                outputDirectory,
                maxColors,
                enableDithering,
                _localization);
            _capturePreview.Closed += OnCapturePreviewClosed;
            _capturePreview.Show();
        }
        catch
        {
            ScreenRecorder.DisposeFrames(frames);
            throw;
        }
    }

    private void OnCaptureOverlayClosed(object? sender, EventArgs e)
    {
        if (sender is CaptureOverlayWindow overlay)
        {
            overlay.RecordingRequested -= OnCaptureRecordingRequested;
            overlay.StopRecordingRequested -= OnCaptureStopRecordingRequested;
            overlay.Closed -= OnCaptureOverlayClosed;
        }

        _captureOverlay = null;
    }

    private void OnCapturePreviewClosed(object? sender, EventArgs e)
    {
        if (sender is CapturePreviewWindow preview)
        {
            preview.Closed -= OnCapturePreviewClosed;
        }

        _capturePreview = null;
        RestoreMainWindowAfterCapture();
    }

    private void RestoreMainWindowAfterCapture()
    {
        if (_restoreMainWindowAfterCapture && !_isExiting)
        {
            ShowMainWindow();
        }

        _restoreMainWindowAfterCapture = false;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_openTrayItem is not null)
        {
            _openTrayItem.Text = _localization?.Get("TrayOpen") ?? "Open";
        }

        if (_exitTrayItem is not null)
        {
            _exitTrayItem.Text = _localization?.Get("TrayExit") ?? "Exit";
        }
    }

    private void ShowTrayNotification(string title, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _notifyIcon?.ShowBalloonTip(2500, title, message, WinForms.ToolTipIcon.Info);
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        _mainWindow?.Hide();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        Shutdown(0);
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _recordingCancellation?.Cancel();
        _capturePreview?.Close();
        _captureOverlay?.Close();
        _captureOverlay = null;

        if (_localization is not null)
        {
            _localization.LanguageChanged -= OnLanguageChanged;
            _localization = null;
        }

        if (_hotkeyHost is not null)
        {
            _hotkeyHost.HotkeyPressed -= OnCaptureHotkeyPressed;
            _hotkeyHost.Dispose();
            _hotkeyHost = null;
        }

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _trayIcon?.Dispose();
        _trayIcon = null;

        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
    }
}
