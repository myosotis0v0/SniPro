using SniPro.Core;

namespace SniPro.Tests;

public class SettingsTests
{
    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        var path = CreateSettingsPath();

        try
        {
            var settings = new JsonSettingsStore(path).Load();

            Assert.Equal(10, settings.FrameRate);
            Assert.Equal(100, settings.ScalePercent);
            Assert.Equal(256, settings.MaxColors);
            Assert.False(settings.EnableDithering);
            Assert.False(settings.StartWithWindows);
            Assert.Equal("Ctrl+Shift+G", settings.CaptureHotkey);
            Assert.Equal(LanguageCodes.System, settings.LanguageCode);
        }
        finally
        {
            DeleteSettingsPath(path);
        }
    }

    [Fact]
    public void SaveThenLoad_PreservesSettings()
    {
        var path = CreateSettingsPath();

        try
        {
            var expected = AppSettingsDefaults.Create();
            expected.OutputDirectory = "C:\\SniPro\\Output";
            expected.FrameRate = 15;
            expected.ScalePercent = 75;
            expected.MaxColors = 128;
            expected.EnableDithering = true;
            expected.StartWithWindows = true;
            expected.CaptureHotkey = "Ctrl+Shift+F12";
            expected.LanguageCode = LanguageCodes.SimplifiedChinese;

            var store = new JsonSettingsStore(path);
            store.Save(expected);
            var actual = store.Load();

            Assert.Equal(expected.OutputDirectory, actual.OutputDirectory);
            Assert.Equal(expected.FrameRate, actual.FrameRate);
            Assert.Equal(expected.ScalePercent, actual.ScalePercent);
            Assert.Equal(expected.MaxColors, actual.MaxColors);
            Assert.True(actual.EnableDithering);
            Assert.True(actual.StartWithWindows);
            Assert.Equal(expected.CaptureHotkey, actual.CaptureHotkey);
            Assert.Equal(expected.LanguageCode, actual.LanguageCode);
        }
        finally
        {
            DeleteSettingsPath(path);
        }
    }

    [Fact]
    public void MalformedJson_ReturnsDefaults()
    {
        var path = CreateSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not valid json");

        try
        {
            var settings = new JsonSettingsStore(path).Load();

            Assert.Equal(10, settings.FrameRate);
            Assert.Equal(256, settings.MaxColors);
        }
        finally
        {
            DeleteSettingsPath(path);
        }
    }

    [Fact]
    public void InvalidValues_AreNormalized()
    {
        var settings = AppSettingsValidator.Normalize(new AppSettings
        {
            OutputDirectory = " ",
            FrameRate = 99,
            ScalePercent = 101,
            MaxColors = 17,
            CaptureHotkey = " ",
            LanguageCode = " "
        });

        Assert.Equal(30, settings.FrameRate);
        Assert.Equal(100, settings.ScalePercent);
        Assert.Equal(256, settings.MaxColors);
        Assert.False(string.IsNullOrWhiteSpace(settings.OutputDirectory));
        Assert.Equal("Ctrl+Shift+G", settings.CaptureHotkey);
        Assert.Equal(LanguageCodes.System, settings.LanguageCode);
    }

    private static string CreateSettingsPath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "SniPro.Tests",
            Guid.NewGuid().ToString("N"),
            "settings.json");
    }

    private static void DeleteSettingsPath(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
