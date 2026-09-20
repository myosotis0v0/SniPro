using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SniPro.Windows;

internal static class GifPaletteQuantizer
{
    private const int MaxHistogramSamples = 250_000;
    private const int PaletteLookupBits = 5;
    private const int PaletteLookupSize = 1 << (PaletteLookupBits * 3);

    public static GifPalette Create(
        IReadOnlyList<Bitmap> frames,
        int startFrame,
        int endFrame,
        int maxColors,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxColors, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxColors, 256);

        var histogram = BuildHistogram(
            frames,
            startFrame,
            endFrame,
            cancellationToken);
        if (histogram.Count == 0)
        {
            return new GifPalette(new[] { new GifRgb(0, 0, 0) });
        }

        var colors = histogram
            .Select(pair => new WeightedColor(
                (byte)((pair.Key >> 16) & 0xff),
                (byte)((pair.Key >> 8) & 0xff),
                (byte)(pair.Key & 0xff),
                pair.Value))
            .ToList();
        var boxes = new List<ColorBox> { new(colors) };

        while (boxes.Count < maxColors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var splitIndex = FindBestSplitIndex(boxes);
            if (splitIndex < 0)
            {
                break;
            }

            var (left, right) = boxes[splitIndex].Split();
            boxes[splitIndex] = left;
            boxes.Add(right);
        }

        var palette = new List<GifRgb>(boxes.Count);
        var uniqueColors = new HashSet<int>();
        foreach (var box in boxes)
        {
            var color = box.GetAverageColor();
            if (uniqueColors.Add(color.PackedRgb))
            {
                palette.Add(color);
            }
        }

        if (palette.Count == 0)
        {
            palette.Add(new GifRgb(0, 0, 0));
        }

        return new GifPalette(palette.ToArray());
    }

    public static Bitmap Quantize(
        Bitmap frame,
        GifPalette palette,
        bool enableDithering,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(palette);
        cancellationToken.ThrowIfCancellationRequested();

        using var source = CreateArgbBitmap(frame);
        var indexed = new Bitmap(
            source.Width,
            source.Height,
            PixelFormat.Format8bppIndexed);
        ApplyPalette(indexed, palette);

        BitmapData? sourceData = null;
        BitmapData? destinationData = null;
        try
        {
            var rectangle = new Rectangle(0, 0, source.Width, source.Height);
            sourceData = source.LockBits(
                rectangle,
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            destinationData = indexed.LockBits(
                rectangle,
                ImageLockMode.WriteOnly,
                PixelFormat.Format8bppIndexed);

            var sourceRow = new byte[checked(source.Width * sizeof(int))];
            var destinationRow = new byte[Math.Abs(destinationData.Stride)];
            var mapper = new PaletteMapper(palette.Colors);
            var currentRedErrors = enableDithering ? new int[source.Width + 2] : null;
            var currentGreenErrors = enableDithering ? new int[source.Width + 2] : null;
            var currentBlueErrors = enableDithering ? new int[source.Width + 2] : null;
            var nextRedErrors = enableDithering ? new int[source.Width + 2] : null;
            var nextGreenErrors = enableDithering ? new int[source.Width + 2] : null;
            var nextBlueErrors = enableDithering ? new int[source.Width + 2] : null;

            for (var y = 0; y < source.Height; y++)
            {
                if ((y & 15) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                Marshal.Copy(
                    GetRowPointer(sourceData, y),
                    sourceRow,
                    0,
                    sourceRow.Length);
                Array.Clear(destinationRow);

                for (var x = 0; x < source.Width; x++)
                {
                    var sourceOffset = x * sizeof(int);
                    var blue = sourceRow[sourceOffset];
                    var green = sourceRow[sourceOffset + 1];
                    var red = sourceRow[sourceOffset + 2];

                    if (enableDithering)
                    {
                        red = ClampToByte(red + currentRedErrors![x + 1]);
                        green = ClampToByte(green + currentGreenErrors![x + 1]);
                        blue = ClampToByte(blue + currentBlueErrors![x + 1]);
                    }

                    var paletteIndex = mapper.FindNearest(red, green, blue);
                    destinationRow[x] = paletteIndex;

                    if (enableDithering)
                    {
                        var paletteColor = palette.Colors[paletteIndex];
                        var redError = red - paletteColor.R;
                        var greenError = green - paletteColor.G;
                        var blueError = blue - paletteColor.B;

                        currentRedErrors![x + 2] += redError * 7 / 16;
                        currentGreenErrors![x + 2] += greenError * 7 / 16;
                        currentBlueErrors![x + 2] += blueError * 7 / 16;
                        nextRedErrors![x] += redError * 3 / 16;
                        nextGreenErrors![x] += greenError * 3 / 16;
                        nextBlueErrors![x] += blueError * 3 / 16;
                        nextRedErrors[x + 1] += redError * 5 / 16;
                        nextGreenErrors[x + 1] += greenError * 5 / 16;
                        nextBlueErrors[x + 1] += blueError * 5 / 16;
                        nextRedErrors[x + 2] += redError / 16;
                        nextGreenErrors[x + 2] += greenError / 16;
                        nextBlueErrors[x + 2] += blueError / 16;
                    }
                }

                Marshal.Copy(
                    destinationRow,
                    0,
                    GetRowPointer(destinationData, y),
                    destinationRow.Length);

                if (enableDithering)
                {
                    (currentRedErrors, nextRedErrors) = (nextRedErrors, currentRedErrors);
                    (currentGreenErrors, nextGreenErrors) = (nextGreenErrors, currentGreenErrors);
                    (currentBlueErrors, nextBlueErrors) = (nextBlueErrors, currentBlueErrors);
                    Array.Clear(nextRedErrors!);
                    Array.Clear(nextGreenErrors!);
                    Array.Clear(nextBlueErrors!);
                }
            }
        }
        catch
        {
            indexed.Dispose();
            throw;
        }
        finally
        {
            if (destinationData is not null)
            {
                indexed.UnlockBits(destinationData);
            }

            if (sourceData is not null)
            {
                source.UnlockBits(sourceData);
            }
        }

        return indexed;
    }

    private static Dictionary<int, long> BuildHistogram(
        IReadOnlyList<Bitmap> frames,
        int startFrame,
        int endFrame,
        CancellationToken cancellationToken)
    {
        ValidateFrameRange(frames, startFrame, endFrame);

        var totalPixels = 0L;
        for (var index = startFrame; index <= endFrame; index++)
        {
            var frame = frames[index];
            totalPixels = checked(totalPixels + (long)frame.Width * frame.Height);
        }

        var sampleStep = Math.Max(
            1L,
            (totalPixels + MaxHistogramSamples - 1) / MaxHistogramSamples);
        var sampleWeight = sampleStep;
        var histogram = new Dictionary<int, long>();
        var samplesTaken = 0;

        for (var index = startFrame; index <= endFrame && samplesTaken < MaxHistogramSamples; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var source = CreateArgbBitmap(frames[index]);
            var rectangle = new Rectangle(0, 0, source.Width, source.Height);
            var data = source.LockBits(
                rectangle,
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[checked(source.Width * sizeof(int))];
                var framePixels = (long)source.Width * source.Height;
                var position = sampleStep > 1 ? sampleStep / 2 : 0;
                var lastRow = -1;

                while (position < framePixels && samplesTaken < MaxHistogramSamples)
                {
                    var y = (int)(position / source.Width);
                    var x = (int)(position % source.Width);
                    if (y != lastRow)
                    {
                        Marshal.Copy(
                            GetRowPointer(data, y),
                            row,
                            0,
                            row.Length);
                        lastRow = y;
                    }

                    var offset = x * sizeof(int);
                    var packedRgb = (row[offset + 2] << 16)
                        | (row[offset + 1] << 8)
                        | row[offset];
                    histogram.TryGetValue(packedRgb, out var count);
                    histogram[packedRgb] = checked(count + sampleWeight);
                    samplesTaken++;
                    position += sampleStep;
                }
            }
            finally
            {
                source.UnlockBits(data);
            }
        }

        return histogram;
    }

    private static int FindBestSplitIndex(IReadOnlyList<ColorBox> boxes)
    {
        var bestIndex = -1;
        var bestRange = -1;
        long bestWeight = -1;

        for (var index = 0; index < boxes.Count; index++)
        {
            var box = boxes[index];
            if (!box.CanSplit)
            {
                continue;
            }

            if (box.LongestRange > bestRange ||
                (box.LongestRange == bestRange && box.Weight > bestWeight))
            {
                bestIndex = index;
                bestRange = box.LongestRange;
                bestWeight = box.Weight;
            }
        }

        return bestIndex;
    }

    private static Bitmap CreateArgbBitmap(Bitmap source)
    {
        var normalized = new Bitmap(
            source.Width,
            source.Height,
            PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(normalized);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(source, 0, 0);
            return normalized;
        }
        catch
        {
            normalized.Dispose();
            throw;
        }
    }

    private static void ApplyPalette(Bitmap indexed, GifPalette palette)
    {
        var colorPalette = indexed.Palette;
        for (var index = 0; index < colorPalette.Entries.Length; index++)
        {
            var color = palette.Colors[Math.Min(index, palette.Colors.Length - 1)];
            colorPalette.Entries[index] = Color.FromArgb(255, color.R, color.G, color.B);
        }

        indexed.Palette = colorPalette;
    }

    private static IntPtr GetRowPointer(BitmapData data, int row)
    {
        return IntPtr.Add(data.Scan0, checked(row * data.Stride));
    }

    private static byte ClampToByte(int value)
    {
        return (byte)Math.Clamp(value, 0, 255);
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
    }

    private readonly record struct WeightedColor(
        byte R,
        byte G,
        byte B,
        long Weight);

    private sealed class ColorBox
    {
        public ColorBox(List<WeightedColor> colors)
        {
            Colors = colors;
            Weight = colors.Sum(color => color.Weight);
            MinRed = colors.Min(color => color.R);
            MaxRed = colors.Max(color => color.R);
            MinGreen = colors.Min(color => color.G);
            MaxGreen = colors.Max(color => color.G);
            MinBlue = colors.Min(color => color.B);
            MaxBlue = colors.Max(color => color.B);
        }

        public List<WeightedColor> Colors { get; }

        public long Weight { get; }

        public byte MinRed { get; }

        public byte MaxRed { get; }

        public byte MinGreen { get; }

        public byte MaxGreen { get; }

        public byte MinBlue { get; }

        public byte MaxBlue { get; }

        public int LongestRange => Math.Max(
            MaxRed - MinRed,
            Math.Max(MaxGreen - MinGreen, MaxBlue - MinBlue));

        public bool CanSplit => Colors.Count > 1 && LongestRange > 0;

        public (ColorBox Left, ColorBox Right) Split()
        {
            var axis = GetLongestAxis();
            Colors.Sort((left, right) => GetChannel(left, axis).CompareTo(GetChannel(right, axis)));

            var targetWeight = Math.Max(1, Weight / 2);
            var accumulatedWeight = 0L;
            var splitAt = 0;
            while (splitAt < Colors.Count - 1 && accumulatedWeight < targetWeight)
            {
                accumulatedWeight += Colors[splitAt].Weight;
                splitAt++;
            }

            splitAt = Math.Clamp(splitAt, 1, Colors.Count - 1);
            return (
                new ColorBox(Colors.GetRange(0, splitAt)),
                new ColorBox(Colors.GetRange(splitAt, Colors.Count - splitAt)));
        }

        public GifRgb GetAverageColor()
        {
            var red = 0L;
            var green = 0L;
            var blue = 0L;
            foreach (var color in Colors)
            {
                red += color.R * color.Weight;
                green += color.G * color.Weight;
                blue += color.B * color.Weight;
            }

            return new GifRgb(
                (byte)Math.Clamp((red + Weight / 2) / Weight, 0, 255),
                (byte)Math.Clamp((green + Weight / 2) / Weight, 0, 255),
                (byte)Math.Clamp((blue + Weight / 2) / Weight, 0, 255));
        }

        private int GetLongestAxis()
        {
            var redRange = MaxRed - MinRed;
            var greenRange = MaxGreen - MinGreen;
            var blueRange = MaxBlue - MinBlue;
            if (greenRange > redRange && greenRange >= blueRange)
            {
                return 1;
            }

            return blueRange > redRange ? 2 : 0;
        }

        private static int GetChannel(WeightedColor color, int axis)
        {
            return axis switch
            {
                1 => color.G,
                2 => color.B,
                _ => color.R
            };
        }
    }

    private sealed class PaletteMapper
    {
        private readonly GifRgb[] _colors;
        private readonly byte[] _lookup = new byte[PaletteLookupSize];

        public PaletteMapper(GifRgb[] colors)
        {
            _colors = colors;
            for (var red = 0; red < 1 << PaletteLookupBits; red++)
            {
                for (var green = 0; green < 1 << PaletteLookupBits; green++)
                {
                    for (var blue = 0; blue < 1 << PaletteLookupBits; blue++)
                    {
                        var key = (red << (PaletteLookupBits * 2))
                            | (green << PaletteLookupBits)
                            | blue;
                        _lookup[key] = FindNearest(
                            ExpandLookupChannel(red),
                            ExpandLookupChannel(green),
                            ExpandLookupChannel(blue));
                    }
                }
            }
        }

        public byte FindNearest(int red, int green, int blue)
        {
            var redKey = (red * 31 + 127) / 255;
            var greenKey = (green * 31 + 127) / 255;
            var blueKey = (blue * 31 + 127) / 255;
            var key = (redKey << (PaletteLookupBits * 2))
                | (greenKey << PaletteLookupBits)
                | blueKey;
            return _lookup[key];
        }

        private byte FindNearest(byte red, byte green, byte blue)
        {
            var bestIndex = 0;
            var bestDistance = int.MaxValue;
            for (var index = 0; index < _colors.Length; index++)
            {
                var color = _colors[index];
                var redDistance = red - color.R;
                var greenDistance = green - color.G;
                var blueDistance = blue - color.B;
                var distance = redDistance * redDistance
                    + greenDistance * greenDistance
                    + blueDistance * blueDistance;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = index;
                }
            }

            return (byte)bestIndex;
        }

        private static byte ExpandLookupChannel(int value)
        {
            return (byte)((value * 255 + 15) / 31);
        }
    }
}

internal sealed class GifPalette
{
    public GifPalette(GifRgb[] colors)
    {
        Colors = colors;
    }

    public GifRgb[] Colors { get; }
}

internal readonly record struct GifRgb(byte R, byte G, byte B)
{
    public int PackedRgb => (R << 16) | (G << 8) | B;
}
