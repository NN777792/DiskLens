namespace DiskCleanerAssistant.Utilities;

public static class StartupDiagnostics
{
    private static readonly object SyncRoot = new();

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiskLens",
        "logs");

    public static void Mark(string message)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                var line = $"{DateTimeOffset.Now:O} | {message}{Environment.NewLine}";
                File.AppendAllText(Path.Combine(LogDirectory, "startup.log"), line);
            }
        }
        catch
        {
            // Diagnostics must never prevent the application from starting.
        }
    }
}
