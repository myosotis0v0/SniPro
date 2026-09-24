using SniPro.Core;

namespace SniPro.Windows;

public enum CaptureRegionHit
{
    Outside,
    Inside,
    Left,
    Top,
    Right,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public static class CaptureRegionEditor
{
    public static CaptureRegionHit HitTest(CaptureRegion region, int x, int y, int tolerance)
    {
        if (!region.IsValid)
        {
            return CaptureRegionHit.Outside;
        }

        tolerance = Math.Max(0, tolerance);
        var withinX = x >= region.X - tolerance && x <= region.Right + tolerance;
        var withinY = y >= region.Y - tolerance && y <= region.Bottom + tolerance;
        if (!withinX || !withinY)
        {
            return CaptureRegionHit.Outside;
        }

        var leftDistance = Math.Abs(x - region.X);
        var rightDistance = Math.Abs(x - region.Right);
        var topDistance = Math.Abs(y - region.Y);
        var bottomDistance = Math.Abs(y - region.Bottom);
        var left = leftDistance <= tolerance && leftDistance <= rightDistance;
        var right = rightDistance <= tolerance && rightDistance < leftDistance;
        var top = topDistance <= tolerance && topDistance <= bottomDistance;
        var bottom = bottomDistance <= tolerance && bottomDistance < topDistance;

        if (left && top) return CaptureRegionHit.TopLeft;
        if (right && top) return CaptureRegionHit.TopRight;
        if (left && bottom) return CaptureRegionHit.BottomLeft;
        if (right && bottom) return CaptureRegionHit.BottomRight;
        if (left) return CaptureRegionHit.Left;
        if (right) return CaptureRegionHit.Right;
        if (top) return CaptureRegionHit.Top;
        if (bottom) return CaptureRegionHit.Bottom;

        return x >= region.X && x <= region.Right && y >= region.Y && y <= region.Bottom
            ? CaptureRegionHit.Inside
            : CaptureRegionHit.Outside;
    }

    public static CaptureRegion Move(CaptureRegion region, int deltaX, int deltaY, PixelRect bounds)
    {
        return new CaptureRegion(
            Math.Clamp(region.X + deltaX, bounds.X, bounds.Right - region.Width),
            Math.Clamp(region.Y + deltaY, bounds.Y, bounds.Bottom - region.Height),
            region.Width,
            region.Height);
    }

    public static CaptureRegion Resize(
        CaptureRegion region,
        CaptureRegionHit edge,
        int deltaX,
        int deltaY,
        PixelRect bounds)
    {
        var left = region.X;
        var top = region.Y;
        var right = region.Right;
        var bottom = region.Bottom;
        var minimumWidth = Math.Min(8, region.Width);
        var minimumHeight = Math.Min(8, region.Height);

        if (edge is CaptureRegionHit.Left or CaptureRegionHit.TopLeft or CaptureRegionHit.BottomLeft)
        {
            left = Math.Clamp(left + deltaX, bounds.X, right - minimumWidth);
        }
        if (edge is CaptureRegionHit.Right or CaptureRegionHit.TopRight or CaptureRegionHit.BottomRight)
        {
            right = Math.Clamp(right + deltaX, left + minimumWidth, bounds.Right);
        }
        if (edge is CaptureRegionHit.Top or CaptureRegionHit.TopLeft or CaptureRegionHit.TopRight)
        {
            top = Math.Clamp(top + deltaY, bounds.Y, bottom - minimumHeight);
        }
        if (edge is CaptureRegionHit.Bottom or CaptureRegionHit.BottomLeft or CaptureRegionHit.BottomRight)
        {
            bottom = Math.Clamp(bottom + deltaY, top + minimumHeight, bounds.Bottom);
        }

        return new CaptureRegion(left, top, right - left, bottom - top);
    }
}
