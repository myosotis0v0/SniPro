using System.ComponentModel;
using System.Threading;
using System.Windows;
using SniPro.Core;
using WinForms = System.Windows.Forms;

namespace SniPro.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private MainWindow? _mainWindow;
    private WinForms.NotifyIcon? _notifyIcon;
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
        _mainWindow = new MainWindow();
        _mainWindow.Closing += OnMainWindowClosing;

        CreateTrayIcon();

        if (e.Args.Any(argument => string.Equals(argument, "--show", StringComparison.OrdinalIgnoreCase)))
        {
            ShowMainWindow();
        }
    }

    private void CreateTrayIcon()
    {
        var contextMenu = new WinForms.ContextMenuStrip();
        var openItem = new WinForms.ToolStripMenuItem("Open");
        openItem.Click += (_, _) => ShowMainWindow();

        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();

        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

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
