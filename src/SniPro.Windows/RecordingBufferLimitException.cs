namespace SniPro.Windows;

public sealed class RecordingBufferLimitException : InvalidOperationException
{
    public RecordingBufferLimitException(long limitBytes)
        : base(
            $"The recording buffer limit of {limitBytes / (1024 * 1024)} MB was reached. "
            + "Reduce the capture area or output scale and try again.")
    {
        LimitBytes = limitBytes;
    }

    public long LimitBytes { get; }

    public int LimitMegabytes => checked((int)(LimitBytes / (1024 * 1024)));
}
