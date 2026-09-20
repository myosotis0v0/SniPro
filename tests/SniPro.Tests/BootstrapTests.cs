using SniPro.Core;

namespace SniPro.Tests;

public class BootstrapTests
{
    [Fact]
    public void StageMarker_IsDefined()
    {
        Assert.Equal("stage-03.5-localization", BootstrapInfo.Stage);
    }
}
