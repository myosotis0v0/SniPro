namespace SniPro.Core;

public readonly record struct CaptureRegion(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);

    public int Bottom => checked(Y + Height);

    public bool IsValid => Width > 0 && Height > 0;

    public static bool TryCreate(
        int startX,
        int startY,
        int endX,
        int endY,
        out CaptureRegion region)
    {
        var left = Math.Min(startX, endX);
        var top = Math.Min(startY, endY);
        var right = Math.Max(startX, endX);
        var bottom = Math.Max(startY, endY);
        region = new CaptureRegion(left, top, right - left, bottom - top);
        return region.IsValid;
    }
}
