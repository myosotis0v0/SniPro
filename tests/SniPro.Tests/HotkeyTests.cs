using SniPro.Windows;

namespace SniPro.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Shift+G", "Ctrl+Shift+G")]
    [InlineData("shift+control+f12", "Ctrl+Shift+F12")]
    [InlineData("Alt+1", "Alt+1")]
    public void ValidHotkeys_AreCanonicalized(string input, string expected)
    {
        var parsed = GlobalHotkeyParser.TryParse(input, out var definition, out var error);

        Assert.True(parsed, error);
        Assert.Equal(expected, definition.CanonicalText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("G")]
    [InlineData("Ctrl+Ctrl+G")]
    [InlineData("Ctrl+Mouse1")]
    public void InvalidHotkeys_AreRejected(string input)
    {
        Assert.False(GlobalHotkeyParser.TryParse(input, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void StartupCommandLine_QuotesExecutablePath()
    {
        var commandLine = StartupManager.BuildCommandLine("C:\\Program Files\\SniPro\\SniPro.exe");

        Assert.Equal("\"C:\\Program Files\\SniPro\\SniPro.exe\"", commandLine);
    }
}
