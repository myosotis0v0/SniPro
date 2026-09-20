using System.Windows;
using SniPro.Core;
using WinForms = System.Windows.Forms;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace SniPro.App;

public partial class MainWindow : Window
{
    private readonly Action<AppSettings> _saveSettings;
    private AppSettings _loadedSettings = AppSettingsDefaults.Create();

    public MainWindow(AppSettings initialSettings, Action<AppSettings> saveSettings)
    {
        _saveSettings = saveSettings;

        InitializeComponent();
        MaxColorsComboBox.ItemsSource = new[] { 32, 64, 128, 256 };
        LoadSettings(initialSettings);
    }

    public void LoadSettings(AppSettings settings)
    {
        _loadedSettings = AppSettingsValidator.Normalize(settings);
        OutputDirectoryTextBox.Text = _loadedSettings.OutputDirectory;
        FrameRateTextBox.Text = _loadedSettings.FrameRate.ToString();
        DurationTextBox.Text = _loadedSettings.DurationSeconds.ToString();
        ScalePercentTextBox.Text = _loadedSettings.ScalePercent.ToString();
        MaxColorsComboBox.SelectedItem = _loadedSettings.MaxColors;
        EnableDitheringCheckBox.IsChecked = _loadedSettings.EnableDithering;
        SetStatus("Settings loaded.", WpfBrushes.Gray);
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Choose where GIF files should be saved.",
            SelectedPath = OutputDirectoryTextBox.Text
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            OutputDirectoryTextBox.Text = dialog.SelectedPath;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildSettings(out var settings))
        {
            return;
        }

        try
        {
            _saveSettings(settings);
            _loadedSettings = settings;
            SetStatus("Settings saved.", WpfBrushes.DarkGreen);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not save settings: {exception.Message}", WpfBrushes.DarkRed);
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        LoadSettings(AppSettingsDefaults.Create());
        SetStatus("Defaults loaded. Click Save to persist them.", WpfBrushes.Gray);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private bool TryBuildSettings(out AppSettings settings)
    {
        settings = _loadedSettings.Clone();
        settings.OutputDirectory = OutputDirectoryTextBox.Text.Trim();

        if (!int.TryParse(FrameRateTextBox.Text, out var frameRate) || frameRate is < 1 or > 30)
        {
            SetStatus("Frame rate must be between 1 and 30.", WpfBrushes.DarkRed);
            return false;
        }

        if (!int.TryParse(DurationTextBox.Text, out var duration) || duration is < 1 or > 60)
        {
            SetStatus("Duration must be between 1 and 60 seconds.", WpfBrushes.DarkRed);
            return false;
        }

        if (!int.TryParse(ScalePercentTextBox.Text, out var scale) || scale is < 25 or > 100)
        {
            SetStatus("Output scale must be between 25 and 100 percent.", WpfBrushes.DarkRed);
            return false;
        }

        if (MaxColorsComboBox.SelectedItem is not int maxColors)
        {
            SetStatus("Select a maximum color count.", WpfBrushes.DarkRed);
            return false;
        }

        settings.FrameRate = frameRate;
        settings.DurationSeconds = duration;
        settings.ScalePercent = scale;
        settings.MaxColors = maxColors;
        settings.EnableDithering = EnableDitheringCheckBox.IsChecked == true;
        settings = AppSettingsValidator.Normalize(settings);
        return true;
    }

    private void SetStatus(string message, WpfBrush color)
    {
        StatusText.Text = message;
        StatusText.Foreground = color;
    }
}
