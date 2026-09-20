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
}
