using Microsoft.Win32;

namespace WindowsAppRestarter;

internal sealed class StartupManager(string executablePath)
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WindowsAppRestarter";

    private string StartupCommand => $"\"{executablePath}\"";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;

        return string.Equals(value, StartupCommand, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not open the current user's Run registry key.");

        if (enabled)
        {
            key.SetValue(ValueName, StartupCommand, RegistryValueKind.String);
            return;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
