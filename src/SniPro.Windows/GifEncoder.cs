using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

namespace SniPro.Windows;

public static class GifEncoder
{
    private const int PropertyTagFrameDelay = 0x5100;
    private const int PropertyTagLoopCount = 0x5101;
    private const short PropertyTypeLong = 4;

    public static Task SaveAsync(
        IReadOnlyList<Bitmap> frames,
        int frameRate,
        int startFrame,
        int endFrame,
        string filePath,
        CancellationToken cancellationToken = default,
        int maxColors = 256,
        bool enableDithering = false,
        IProgress<GifEncodingProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxColors, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxColors, 256);
        ValidateFrameRange(frames, startFrame, endFrame);

        return Task.Run(
            () => SaveCore(
                frames,
                frameRate,
                startFrame,
                endFrame,
                filePath,
                maxColors,
                enableDithering,
                cancellationToken,
                progress),
            cancellationToken);
    }

    private static void SaveCore(
        IReadOnlyList<Bitmap> frames,
        int frameRate,
        int startFrame,
        int endFrame,
        string filePath,
        int maxColors,
        bool enableDithering,
        CancellationToken cancellationToken,
        IProgress<GifEncodingProgress>? progress)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The GIF output path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        ValidateFrameDimensions(frames, startFrame, endFrame);
        progress?.Report(new GifEncodingProgress(GifEncodingStage.AnalyzingColors));
        var palette = GifPaletteQuantizer.Create(
            frames,
            startFrame,
            endFrame,
            maxColors,
            cancellationToken);
        progress?.Report(new GifEncodingProgress(GifEncodingStage.PreparingPalette));

        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            EncodeToTemporaryFile(
                frames,
                frameRate,
                startFrame,
                endFrame,
                temporaryPath,
                palette,
                enableDithering,
                cancellationToken,
                progress);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void EncodeToTemporaryFile(
        IReadOnlyList<Bitmap> frames,
        int frameRate,
        int startFrame,
        int endFrame,
        string temporaryPath,
        GifPalette palette,
        bool enableDithering,
        CancellationToken cancellationToken,
        IProgress<GifEncodingProgress>? progress)
    {
        var encoder = GetGifEncoder();
        var frameDelay = Math.Max(1, (int)Math.Round(100d / frameRate));
        var frameCount = endFrame - startFrame + 1;
        var mapper = GifPaletteQuantizer.CreateMapper(palette);
        progress?.Report(new GifEncodingProgress(GifEncodingStage.EncodingFrames, 0, frameCount));

        using var firstFrame = GifPaletteQuantizer.Quantize(
            frames[startFrame],
            palette,
            mapper,
            enableDithering,
            cancellationToken);
        SetAnimationMetadata(firstFrame, frameCount, frameDelay);

        using (var parameters = CreateSaveParameters(EncoderValue.MultiFrame))
        {
            firstFrame.Save(temporaryPath, encoder, parameters);
        }
        progress?.Report(new GifEncodingProgress(GifEncodingStage.EncodingFrames, 1, frameCount));

        var reportInterval = (frameCount - 1) / 100 + 1;
        for (var index = startFrame + 1; index <= endFrame; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var frame = GifPaletteQuantizer.Quantize(
                frames[index],
                palette,
                mapper,
                enableDithering,
                cancellationToken);
            using var parameters = CreateSaveParameters(EncoderValue.FrameDimensionTime);
            firstFrame.SaveAdd(frame, parameters);
            var completedFrames = index - startFrame + 1;
            if (completedFrames == frameCount || completedFrames % reportInterval == 0)
            {
                progress?.Report(new GifEncodingProgress(
                    GifEncodingStage.EncodingFrames,
                    completedFrames,
                    frameCount));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new GifEncodingProgress(GifEncodingStage.Finalizing));
        using var flushParameters = CreateSaveParameters(EncoderValue.Flush);
        firstFrame.SaveAdd(flushParameters);
    }

    private static void SetAnimationMetadata(Bitmap firstFrame, int frameCount, int frameDelay)
    {
        var delays = new byte[checked(frameCount * sizeof(int))];
        for (var index = 0; index < frameCount; index++)
        {
            BitConverter.GetBytes(frameDelay).CopyTo(delays, index * sizeof(int));
        }

        firstFrame.SetPropertyItem(CreatePropertyItem(
            PropertyTagFrameDelay,
            PropertyTypeLong,
            delays));
        firstFrame.SetPropertyItem(CreatePropertyItem(
            PropertyTagLoopCount,
            PropertyTypeLong,
            BitConverter.GetBytes(0)));
    }

    private static PropertyItem CreatePropertyItem(int id, short type, byte[] value)
    {
        var property = Activator.CreateInstance(
            typeof(PropertyItem),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: null,
            culture: null) as PropertyItem;
        if (property is null)
        {
            throw new InvalidOperationException("Could not create GIF metadata.");
        }

        property.Id = id;
        property.Type = type;
        property.Len = value.Length;
        property.Value = value;
        return property;
    }

    private static EncoderParameters CreateSaveParameters(EncoderValue saveFlag)
    {
        var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(
            Encoder.SaveFlag,
            (long)saveFlag);
        return parameters;
    }

    private static ImageCodecInfo GetGifEncoder()
    {
        return ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(codec => string.Equals(
                codec.MimeType,
                "image/gif",
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The Windows GIF encoder is unavailable.");
    }

    private static void ValidateFrameRange(
        IReadOnlyList<Bitmap> frames,
        int startFrame,
        int endFrame)
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one frame is required.", nameof(frames));
        }

        if (startFrame < 0 || endFrame < startFrame || endFrame >= frames.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startFrame),
                "The selected GIF frame range is invalid.");
        }

        for (var index = startFrame; index <= endFrame; index++)
        {
            if (frames[index] is null)
            {
                throw new ArgumentException("The selected frame range contains a null frame.", nameof(frames));
            }
        }
    }

    private static void ValidateFrameDimensions(
        IReadOnlyList<Bitmap> frames,
        int startFrame,
        int endFrame)
    {
        var width = frames[startFrame].Width;
        var height = frames[startFrame].Height;
        for (var index = startFrame + 1; index <= endFrame; index++)
        {
            if (frames[index].Width != width || frames[index].Height != height)
            {
                throw new ArgumentException(
                    "All frames in the selected GIF range must have the same dimensions.",
                    nameof(frames));
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Preserve the original encoding or cancellation error.
        }
    }
}
