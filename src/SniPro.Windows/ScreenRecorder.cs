using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SniPro.Core;

namespace SniPro.Windows;

public static class ScreenRecorder
{
    public const long MaxBufferedBytes = 512L * 1024 * 1024;

    public static Task<IReadOnlyList<Bitmap>> RecordAsync(
        CaptureRegion region,
        int frameRate,
        int scalePercent,
        CancellationToken stopToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(region.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(region.Height);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(scalePercent, 1);

        return Task.Run(
            () => RecordCoreAsync(region, frameRate, scalePercent, stopToken));
    }

    private static async Task<IReadOnlyList<Bitmap>> RecordCoreAsync(
        CaptureRegion region,
        int frameRate,
        int scalePercent,
        CancellationToken stopToken)
    {
        var frames = new List<Bitmap>();
        var stopwatch = Stopwatch.StartNew();
        var (outputWidth, outputHeight) = GetOutputSize(region, scalePercent);
        var estimatedFrameBytes = checked(
            (long)outputWidth * outputHeight * sizeof(int));

        try
        {
            while (frames.Count == 0 || !stopToken.IsCancellationRequested)
            {
                var bufferedBytes = checked((frames.Count + 1L) * estimatedFrameBytes);
                if (bufferedBytes > MaxBufferedBytes)
                {
                    throw new InvalidOperationException(
                        $"The recording buffer limit of {MaxBufferedBytes / (1024 * 1024)} MB was reached. "
                        + "Reduce the capture area or output scale and try again.");
                }

                frames.Add(CaptureFrame(region, scalePercent));

                var nextFrameTime = TimeSpan.FromSeconds(frames.Count / (double)frameRate);
                var remaining = nextFrameTime - stopwatch.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await WaitForNextFrameAsync(remaining, stopToken).ConfigureAwait(false);
                }
            }

            return frames;
        }
        catch
        {
            DisposeFrames(frames);
            throw;
        }
    }

    private static async Task WaitForNextFrameAsync(
        TimeSpan remaining,
        CancellationToken stopToken)
    {
        while (remaining > TimeSpan.Zero && !stopToken.IsCancellationRequested)
        {
            var delay = remaining > TimeSpan.FromMilliseconds(25)
                ? TimeSpan.FromMilliseconds(25)
                : remaining;
            await Task.Delay(delay).ConfigureAwait(false);
            remaining -= delay;
        }
    }

    private static Bitmap CaptureFrame(CaptureRegion region, int scalePercent)
    {
        var source = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppPArgb);
        var transferSourceOwnership = false;
        try
        {
            using (var graphics = Graphics.FromImage(source))
            {
                graphics.CopyFromScreen(
                    region.X,
                    region.Y,
                    0,
                    0,
                    new Size(region.Width, region.Height),
                    CopyPixelOperation.SourceCopy);
            }

            if (scalePercent == 100)
            {
                transferSourceOwnership = true;
                return source;
            }

            var (scaledWidth, scaledHeight) = GetOutputSize(region, scalePercent);
            var scaled = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format32bppPArgb);
            try
            {
                using var scaledGraphics = Graphics.FromImage(scaled);
                scaledGraphics.CompositingMode = CompositingMode.SourceCopy;
                scaledGraphics.CompositingQuality = CompositingQuality.HighQuality;
                scaledGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                scaledGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                scaledGraphics.DrawImage(source, new Rectangle(0, 0, scaledWidth, scaledHeight));
                return scaled;
            }
            catch
            {
                scaled.Dispose();
                throw;
            }
        }
        finally
        {
            if (!transferSourceOwnership)
            {
                source.Dispose();
            }
        }
    }

    private static (int Width, int Height) GetOutputSize(
        CaptureRegion region,
        int scalePercent)
    {
        var width = checked((int)Math.Max(
            1L,
            (long)region.Width * scalePercent / 100));
        var height = checked((int)Math.Max(
            1L,
            (long)region.Height * scalePercent / 100));
        return (width, height);
    }

    public static void DisposeFrames(IEnumerable<Bitmap> frames)
    {
        foreach (var frame in frames)
        {
            frame.Dispose();
        }
    }
}
