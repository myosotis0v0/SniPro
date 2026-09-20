using System.Drawing;
using System.Drawing.Imaging;
using SniPro.Windows;

namespace SniPro.Tests;

public class GifEncoderTests
{
    [Fact]
    public async Task SaveAsync_WritesSelectedFramesAsAnimatedGif()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SniPro.Tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "capture.gif");
        Directory.CreateDirectory(directory);

        using var first = new Bitmap(8, 8);
        using var second = new Bitmap(8, 8);
        using (var firstGraphics = Graphics.FromImage(first))
        {
            firstGraphics.Clear(Color.Red);
        }

        using (var secondGraphics = Graphics.FromImage(second))
        {
            secondGraphics.Clear(Color.Blue);
        }

        try
        {
            await GifEncoder.SaveAsync(
                new[] { first, second },
                frameRate: 10,
                startFrame: 0,
                endFrame: 1,
                path);

            Assert.True(File.Exists(path));
            using var image = Image.FromFile(path);
            Assert.Equal(2, image.GetFrameCount(FrameDimension.Time));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveAsync_RespectsMaximumColorCountWithDithering()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SniPro.Tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "limited-palette.gif");
        Directory.CreateDirectory(directory);

        using var frame = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.Red);
            graphics.FillRectangle(Brushes.Green, 8, 0, 8, 8);
            graphics.FillRectangle(Brushes.Blue, 0, 8, 8, 8);
            graphics.FillRectangle(Brushes.White, 8, 8, 8, 8);
        }

        try
        {
            await GifEncoder.SaveAsync(
                new[] { frame },
                frameRate: 10,
                startFrame: 0,
                endFrame: 0,
                filePath: path,
                maxColors: 2,
                enableDithering: true);

            using var image = Image.FromFile(path);
            using var rendered = new Bitmap(image);
            var colors = new HashSet<int>();
            for (var y = 0; y < rendered.Height; y++)
            {
                for (var x = 0; x < rendered.Width; x++)
                {
                    colors.Add(rendered.GetPixel(x, y).ToArgb());
                }
            }

            Assert.True(colors.Count <= 2, $"The GIF contained {colors.Count} colors.");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
