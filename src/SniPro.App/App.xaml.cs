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
    private JsonSettingsStore? _settingsStore;
    private StartupManager? _startupManager;
    private GlobalHotkeyHost? _hotkeyHost;
    private LocalizationService? _localization;
    private WinForms.ToolStripMenuItem? _openTrayItem;
    private WinForms.ToolStripMenuItem? _exitTrayItem;
    private AppSettings _settings = AppSettingsDefaults.Create();
    private bool _isExiting;

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

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = SniProIdentity.Name,
            ContextMenuStrip = contextMenu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
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
        if (_mainWindow?.IsVisible == true)
        {
            _mainWindow.ShowCaptureHotkeyReceived();
        }
        else
        {
            ShowTrayNotification(
                _localization?.Get("TrayCaptureHotkeyTitle") ?? "Capture hotkey received",
                _localization?.Get("TrayCaptureHotkeyMessage") ?? "The capture overlay will be added in the next stage.");
        }
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

        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
    }
}
