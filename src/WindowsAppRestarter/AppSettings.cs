using Microsoft.Win32;

namespace WindowsAppRestarter;

internal sealed class AppSettings
{
    private const string KeyPath = @"Software\WindowsAppRestarter";
    private const string AutoUpdateValueName = "AutoUpdate";

    public bool AutoUpdateEnabled
    {
        get => ReadBool(AutoUpdateValueName, defaultValue: true);
        set => WriteBool(AutoUpdateValueName, value);
    }

    private static bool ReadBool(string name, bool defaultValue)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            return key?.GetValue(name) is int value ? value != 0 : defaultValue;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return defaultValue;
        }
    }

    private static void WriteBool(string name, bool value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not open the Windows App Restarter settings key.");
        key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
    }
}
