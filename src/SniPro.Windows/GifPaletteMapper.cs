namespace SniPro.Windows;

internal sealed class GifPaletteMapper
{
    private const int PaletteLookupBits = 5;
    private const int PaletteLookupSize = 1 << (PaletteLookupBits * 3);

    private readonly GifRgb[] _colors;
    private readonly byte[] _lookup = new byte[PaletteLookupSize];

    public GifPaletteMapper(GifRgb[] colors)
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
