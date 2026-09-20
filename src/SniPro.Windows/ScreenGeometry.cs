using System.Runtime.InteropServices;

namespace SniPro.Windows;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);

    public int Bottom => checked(Y + Height);
}

public static class ScreenGeometry
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    public static PixelRect GetVirtualScreenBounds()
    {
        return new PixelRect(
            GetSystemMetrics(SmXVirtualScreen),
            GetSystemMetrics(SmYVirtualScreen),
            GetSystemMetrics(SmCxVirtualScreen),
            GetSystemMetrics(SmCyVirtualScreen));
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
