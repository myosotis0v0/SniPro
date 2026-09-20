namespace SniPro.Core;

public static class AppSettingsDefaults
{
    public static AppSettings Create()
    {
        var videosDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        var fallbackDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var outputDirectory = string.IsNullOrWhiteSpace(videosDirectory)
            ? Path.Combine(fallbackDirectory, "SniPro", "Output")
            : Path.Combine(videosDirectory, "SniPro");

        return new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            OutputDirectory = outputDirectory,
            FrameRate = 10,
            DurationSeconds = 5,
            ScalePercent = 100,
            MaxColors = 256,
            EnableDithering = false,
            StartWithWindows = false,
            CaptureHotkey = "Ctrl+Shift+G",
            LanguageCode = LanguageCodes.System
        };
    }
}
