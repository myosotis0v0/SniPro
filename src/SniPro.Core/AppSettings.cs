namespace SniPro.Core;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string OutputDirectory { get; set; } = string.Empty;

    public int FrameRate { get; set; } = 10;

    public int DurationSeconds { get; set; } = 5;

    public int ScalePercent { get; set; } = 100;

    public int MaxColors { get; set; } = 256;

    public bool EnableDithering { get; set; }

    public AppSettings Clone()
    {
        return new AppSettings
        {
            SchemaVersion = SchemaVersion,
            OutputDirectory = OutputDirectory,
            FrameRate = FrameRate,
            DurationSeconds = DurationSeconds,
            ScalePercent = ScalePercent,
            MaxColors = MaxColors,
            EnableDithering = EnableDithering
        };
    }
}
