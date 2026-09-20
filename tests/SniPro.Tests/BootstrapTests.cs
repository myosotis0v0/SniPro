using SniPro.Core;

namespace SniPro.Tests;

public class BootstrapTests
{
    [Fact]
    public void StageMarker_IsDefined()
    {
        Assert.Equal("stage-01-tray-shell", BootstrapInfo.Stage);
    }
}
