using Microsoft.Win32;

namespace SniPro.Windows;

public sealed class StartupManager
{
    public const string RunRegistryPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

    private readonly string _valueName;
    private readonly string _commandLine;

    public StartupManager(string valueName, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(valueName))
        {
            throw new ArgumentException("A registry value name is required.", nameof(valueName));
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("An executable path is required.", nameof(executablePath));
        }

        _valueName = valueName;
        _commandLine = BuildCommandLine(executablePath);
    }

    public string CommandLine => _commandLine;

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: false);
        return key?.GetValue(_valueName) is string value &&
               string.Equals(value, _commandLine, StringComparison.OrdinalIgnoreCase);
    }

    public void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunRegistryPath);
        if (key is null)
        {
            throw new InvalidOperationException("Could not open the Windows startup registry key.");
        }

        if (enabled)
        {
            key.SetValue(_valueName, _commandLine, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(_valueName, throwOnMissingValue: false);
        }
    }

    public static string BuildCommandLine(string executablePath)
    {
        return $"\"{executablePath.Trim().Trim('\"')}\"";
    }
}
