using System.Windows;
using SniPro.Core;
using SniPro.Windows;
using WinForms = System.Windows.Forms;
using WpfBrush = System.Windows.Media.Brush;

namespace SniPro.App;

public partial class MainWindow : Window
{
    private readonly Action<AppSettings> _saveSettings;
    private readonly LocalizationService _localization;
    private AppSettings _loadedSettings = AppSettingsDefaults.Create();

    public MainWindow(
        AppSettings initialSettings,
        Action<AppSettings> saveSettings,
        LocalizationService localization)
    {
        _saveSettings = saveSettings;
        _localization = localization;

        InitializeComponent();
        VersionText.Text = $"v{SniProIdentity.Version}";
        MaxColorsComboBox.ItemsSource = new[] { 32, 64, 128, 256 };
        LoadSettings(initialSettings);
    }

    public void LoadSettings(AppSettings settings)
    {
        _loadedSettings = AppSettingsValidator.Normalize(settings);
        OutputDirectoryTextBox.Text = _loadedSettings.OutputDirectory;
        FrameRateTextBox.Text = _loadedSettings.FrameRate.ToString();
        ScalePercentTextBox.Text = _loadedSettings.ScalePercent.ToString();
        MaxColorsComboBox.SelectedItem = _loadedSettings.MaxColors;
        EnableDitheringCheckBox.IsChecked = _loadedSettings.EnableDithering;
        StartWithWindowsCheckBox.IsChecked = _loadedSettings.StartWithWindows;
        CaptureHotkeyTextBox.Text = _loadedSettings.CaptureHotkey;
        LanguageComboBox.SelectedValue = IsSupportedLanguageCode(_loadedSettings.LanguageCode)
            ? _loadedSettings.LanguageCode
            : LanguageCodes.System;
        SetStatus(_localization.Get("StatusSettingsLoaded"), ThemeBrush("MutedBrush"));
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = _localization.Get("OutputDirectoryLabel"),
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
            SetStatus(_localization.Get("StatusSettingsSaved"), ThemeBrush("SuccessBrush"));
        }
        catch (Exception exception)
        {
            SetStatus(
                _localization.Format("ErrorCouldNotSave", exception.Message),
                ThemeBrush("ErrorBrush"));
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        LoadSettings(AppSettingsDefaults.Create());
        SetStatus(_localization.Get("StatusDefaultsLoaded"), ThemeBrush("MutedBrush"));
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    public void ShowCaptureHotkeyReceived()
    {
        SetStatus(_localization.Get("StatusCaptureHotkeyReceived"), ThemeBrush("AccentBrush"));
    }

    private bool TryBuildSettings(out AppSettings settings)
    {
        settings = _loadedSettings.Clone();
        settings.OutputDirectory = OutputDirectoryTextBox.Text.Trim();

        if (!int.TryParse(FrameRateTextBox.Text, out var frameRate) || frameRate is < 1 or > 30)
        {
            SetStatus(_localization.Get("ErrorFrameRate"), ThemeBrush("ErrorBrush"));
            return false;
        }

        if (!int.TryParse(ScalePercentTextBox.Text, out var scale) || scale is < 25 or > 100)
        {
            SetStatus(_localization.Get("ErrorScale"), ThemeBrush("ErrorBrush"));
            return false;
        }

        if (MaxColorsComboBox.SelectedItem is not int maxColors)
        {
            SetStatus(_localization.Get("ErrorMaxColors"), ThemeBrush("ErrorBrush"));
            return false;
        }

        if (!GlobalHotkeyParser.TryParseWithCode(
                CaptureHotkeyTextBox.Text,
                out var hotkey,
                out var hotkeyError))
        {
            SetStatus(_localization.GetHotkeyError(hotkeyError), ThemeBrush("ErrorBrush"));
            return false;
        }

        settings.FrameRate = frameRate;
        settings.ScalePercent = scale;
        settings.MaxColors = maxColors;
        settings.EnableDithering = EnableDitheringCheckBox.IsChecked == true;
        settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        settings.CaptureHotkey = hotkey.CanonicalText;
        settings.LanguageCode = LanguageComboBox.SelectedValue as string ?? LanguageCodes.System;
        settings = AppSettingsValidator.Normalize(settings);
        return true;
    }

    private static bool IsSupportedLanguageCode(string languageCode)
    {
        return languageCode is LanguageCodes.System or LanguageCodes.English or LanguageCodes.SimplifiedChinese;
    }

    private void SetStatus(string message, WpfBrush color)
    {
        StatusText.Text = message;
        StatusText.Foreground = color;
        UiMotion.FadeIn(StatusText);
    }

    private WpfBrush ThemeBrush(string key) => (WpfBrush)FindResource(key);

    private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            UiMotion.Reveal(SettingsHeader);
            UiMotion.Reveal(SettingsContent, 75);
        }
    }
}
