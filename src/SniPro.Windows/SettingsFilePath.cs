namespace SniPro.Windows;

public static class SettingsFilePath
{
    public static string GetDefault()
    {
        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(applicationData, "SniPro", "settings.json");
    }
}
