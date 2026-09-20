using SniPro.Core;

namespace SniPro.Tests;

public class BootstrapTests
{
    [Fact]
    public void StageMarker_IsDefined()
    {
        Assert.Equal("stage-04-capture-overlay", BootstrapInfo.Stage);
    }

    [Fact]
    public void CaptureRegion_NormalizesDragCoordinates()
    {
        var valid = CaptureRegion.TryCreate(320, 240, 80, 40, out var region);

        Assert.True(valid);
        Assert.Equal(new CaptureRegion(80, 40, 240, 200), region);
        Assert.Equal(320, region.Right);
        Assert.Equal(240, region.Bottom);
    }
}
