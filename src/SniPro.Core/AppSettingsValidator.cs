namespace SniPro.Core;

public static class AppSettingsValidator
{
    public static AppSettings Normalize(AppSettings? settings)
    {
        var defaults = AppSettingsDefaults.Create();
        if (settings is null)
        {
            return defaults;
        }

        var normalized = settings.Clone();
        normalized.SchemaVersion = AppSettings.CurrentSchemaVersion;
        normalized.OutputDirectory = string.IsNullOrWhiteSpace(normalized.OutputDirectory)
            ? defaults.OutputDirectory
            : normalized.OutputDirectory.Trim();
        normalized.FrameRate = Math.Clamp(normalized.FrameRate, 1, 30);
        normalized.ScalePercent = Math.Clamp(normalized.ScalePercent, 25, 100);
        normalized.MaxColors = normalized.MaxColors is 32 or 64 or 128 or 256
            ? normalized.MaxColors
            : defaults.MaxColors;
        normalized.CaptureHotkey = string.IsNullOrWhiteSpace(normalized.CaptureHotkey)
            ? defaults.CaptureHotkey
            : normalized.CaptureHotkey.Trim();
        normalized.LanguageCode = string.IsNullOrWhiteSpace(normalized.LanguageCode)
            ? defaults.LanguageCode
            : normalized.LanguageCode.Trim();

        return normalized;
    }
}
