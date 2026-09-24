using SniPro.Core;
using SniPro.Windows;

namespace SniPro.Tests;

public sealed class CaptureRegionEditorTests
{
    private static readonly CaptureRegion Region = new(100, 200, 300, 180);
    private static readonly PixelRect Bounds = new(-500, 0, 1300, 900);

    [Theory]
    [InlineData(250, 280, CaptureRegionHit.Inside)]
    [InlineData(20, 280, CaptureRegionHit.Outside)]
    [InlineData(100, 280, CaptureRegionHit.Left)]
    [InlineData(400, 280, CaptureRegionHit.Right)]
    [InlineData(250, 200, CaptureRegionHit.Top)]
    [InlineData(250, 380, CaptureRegionHit.Bottom)]
    [InlineData(100, 200, CaptureRegionHit.TopLeft)]
    [InlineData(400, 200, CaptureRegionHit.TopRight)]
    [InlineData(100, 380, CaptureRegionHit.BottomLeft)]
    [InlineData(400, 380, CaptureRegionHit.BottomRight)]
    public void HitTest_DistinguishesInteriorEdgesAndOutside(int x, int y, CaptureRegionHit expected)
    {
        Assert.Equal(expected, CaptureRegionEditor.HitTest(Region, x, y, 8));
    }

    [Fact]
    public void Move_PreservesSizeAndClampsToVirtualScreen()
    {
        Assert.Equal(new CaptureRegion(120, 170, 300, 180),
            CaptureRegionEditor.Move(Region, 20, -30, Bounds));
        Assert.Equal(new CaptureRegion(-500, 720, 300, 180),
            CaptureRegionEditor.Move(Region, -2000, 2000, Bounds));
    }

    [Theory]
    [InlineData(CaptureRegionHit.Left, 20, 0, 120, 200, 280, 180)]
    [InlineData(CaptureRegionHit.Right, 20, 0, 100, 200, 320, 180)]
    [InlineData(CaptureRegionHit.Top, 0, 20, 100, 220, 300, 160)]
    [InlineData(CaptureRegionHit.Bottom, 0, 20, 100, 200, 300, 200)]
    [InlineData(CaptureRegionHit.TopLeft, -20, -20, 80, 180, 320, 200)]
    [InlineData(CaptureRegionHit.BottomRight, 20, 20, 100, 200, 320, 200)]
    public void Resize_AdjustsRequestedEdges(
        CaptureRegionHit edge,
        int deltaX,
        int deltaY,
        int x,
        int y,
        int width,
        int height)
    {
        Assert.Equal(new CaptureRegion(x, y, width, height),
            CaptureRegionEditor.Resize(Region, edge, deltaX, deltaY, Bounds));
    }

    [Fact]
    public void Resize_DoesNotInvertOrEscapeVirtualScreen()
    {
        Assert.Equal(new CaptureRegion(392, 200, 8, 180),
            CaptureRegionEditor.Resize(Region, CaptureRegionHit.Left, 1000, 0, Bounds));
        Assert.Equal(new CaptureRegion(100, 200, 700, 180),
            CaptureRegionEditor.Resize(Region, CaptureRegionHit.Right, 1000, 0, Bounds));
    }
}
