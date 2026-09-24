namespace SniPro.Windows;

public enum GifEncodingStage
{
    AnalyzingColors,
    PreparingPalette,
    EncodingFrames,
    Finalizing
}

public readonly record struct GifEncodingProgress(
    GifEncodingStage Stage,
    int CompletedFrames = 0,
    int TotalFrames = 0);
