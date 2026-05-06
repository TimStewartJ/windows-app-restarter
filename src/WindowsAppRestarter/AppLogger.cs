using System.Diagnostics;

namespace WindowsAppRestarter;

internal static class AppLogger
{
    private static readonly object SyncRoot = new();

    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsAppRestarter");

    public static string LogPath => Path.Combine(LogDirectory, "WindowsAppRestarter.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception exception) =>
        Write("ERROR", $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{level}] {message}{Environment.NewLine}";

            lock (SyncRoot)
            {
                File.AppendAllText(LogPath, line);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to write Windows App Restarter log: {exception}");
        }
    }
}
