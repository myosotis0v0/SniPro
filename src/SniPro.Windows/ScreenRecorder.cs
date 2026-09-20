using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SniPro.Core;

namespace SniPro.Windows;

public static class ScreenRecorder
{
    public static Task<IReadOnlyList<Bitmap>> RecordAsync(
        CaptureRegion region,
        int frameRate,
        int durationSeconds,
        int scalePercent,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(region.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(region.Height);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(durationSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(scalePercent, 1);

        return Task.Run(
            () => RecordCoreAsync(region, frameRate, durationSeconds, scalePercent, cancellationToken),
            cancellationToken);
    }

    private static async Task<IReadOnlyList<Bitmap>> RecordCoreAsync(
        CaptureRegion region,
        int frameRate,
        int durationSeconds,
        int scalePercent,
        CancellationToken cancellationToken)
    {
        var frameCount = checked(frameRate * durationSeconds);
        var frames = new List<Bitmap>(frameCount);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            for (var index = 0; index < frameCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                frames.Add(CaptureFrame(region, scalePercent));

                var nextFrameTime = TimeSpan.FromSeconds((index + 1d) / frameRate);
                var remaining = nextFrameTime - stopwatch.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
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

    private static Bitmap CaptureFrame(CaptureRegion region, int scalePercent)
    {
        using var source = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppPArgb);
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
            return new Bitmap(source);
        }

        var scaledWidth = Math.Max(1, region.Width * scalePercent / 100);
        var scaledHeight = Math.Max(1, region.Height * scalePercent / 100);
        var scaled = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format32bppPArgb);
        using var scaledGraphics = Graphics.FromImage(scaled);
        scaledGraphics.CompositingMode = CompositingMode.SourceCopy;
        scaledGraphics.CompositingQuality = CompositingQuality.HighQuality;
        scaledGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        scaledGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        scaledGraphics.DrawImage(source, new Rectangle(0, 0, scaledWidth, scaledHeight));
        return scaled;
    }

    public static void DisposeFrames(IEnumerable<Bitmap> frames)
    {
        foreach (var frame in frames)
        {
            frame.Dispose();
        }
    }
}
