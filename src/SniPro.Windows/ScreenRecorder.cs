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

        try
        {
            while (frames.Count == 0 || !stopToken.IsCancellationRequested)
            {
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
