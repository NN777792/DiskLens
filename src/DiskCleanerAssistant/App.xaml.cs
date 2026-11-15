using System.Windows;
using System.Windows.Threading;
using DiskCleanerAssistant.Utilities;

namespace DiskCleanerAssistant;

public partial class App : Application
{
    private const string ProductName = "DiskLens";

    public App()
    {
        StartupDiagnostics.Mark("App constructor started");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        StartupDiagnostics.Mark("App constructor completed");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupDiagnostics.Mark("App.OnStartup started");
        base.OnStartup(e);
        StartupDiagnostics.Mark("App.OnStartup completed");
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = WriteErrorLog("界面线程", e.Exception);
        var logMessage = logPath is null
            ? "错误日志未能写入。"
            : $"错误日志：{logPath}";

        MessageBox.Show(
            $"DiskLens 遇到错误，无法继续当前操作。\n\n{e.Exception.Message}\n\n{logMessage}",
            "DiskLens 运行错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteErrorLog("未处理异常", exception);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteErrorLog("后台任务", e.Exception);
        e.SetObserved();
    }

    private static string? WriteErrorLog(string source, Exception exception)
    {
        try
        {
            var logDirectory = StartupDiagnostics.LogDirectory;
            Directory.CreateDirectory(logDirectory);

            var logPath = Path.Combine(
                logDirectory,
                $"error-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
            var content = $"""
                DiskLens error report
                Time: {DateTimeOffset.Now:O}
                Source: {source}
                OS: {Environment.OSVersion}
                Runtime: {Environment.Version}
                64-bit process: {Environment.Is64BitProcess}

                {exception}
                """;
            File.WriteAllText(logPath, content);
            return logPath;
        }
        catch
        {
            return null;
        }
    }
}
