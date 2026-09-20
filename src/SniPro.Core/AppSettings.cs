namespace SniPro.Core;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string OutputDirectory { get; set; } = string.Empty;

    public int FrameRate { get; set; } = 10;

    public int ScalePercent { get; set; } = 100;

    public int MaxColors { get; set; } = 256;

    public bool EnableDithering { get; set; }

    public bool StartWithWindows { get; set; }

    public string CaptureHotkey { get; set; } = "Ctrl+Shift+G";

    public string LanguageCode { get; set; } = LanguageCodes.System;

    public AppSettings Clone()
    {
        return new AppSettings
        {
            SchemaVersion = SchemaVersion,
            OutputDirectory = OutputDirectory,
            FrameRate = FrameRate,
            ScalePercent = ScalePercent,
            MaxColors = MaxColors,
            EnableDithering = EnableDithering,
            StartWithWindows = StartWithWindows,
            CaptureHotkey = CaptureHotkey,
            LanguageCode = LanguageCode
        };
    }
}
