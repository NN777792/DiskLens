using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DiskLens.App.ViewModels;

namespace DiskLens.App;
public partial class App : Application
{
    private readonly bool smokeTest = Environment.GetCommandLineArgs().Contains("--startup-smoke-test");
    private int reportedFailure;
    private static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskLens", "Logs", "startup-latest.log");

    public App()
    {
        WriteLog($"Starting DiskLens {typeof(App).Assembly.GetName().Version}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}", reset: true);
        DispatcherUnhandledException += (_, e) => { e.Handled = true; ReportFailure(e.Exception); Shutdown(1); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportFailure(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject.ToString()));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var window = new MainWindow();
            MainWindow = window;
            if (smokeTest)
            {
                // Force templates containing both read-only percentage bindings to be instantiated.
                window.Model.Drives.Add(new DriveCard("C:\\", "Startup test", "Test", 100, 25));
                window.Model.Categories.Add(new CategoryRow("Startup test", "75 B", "1", 75, Brushes.CornflowerBlue));
                window.ContentRendered += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    window.UpdateLayout();
                    WriteLog("STARTUP_SMOKE_TEST_PASSED");
                    Shutdown(0);
                }));
            }
            window.Show();
            WriteLog("Main window created.");
        }
        catch (Exception ex) { ReportFailure(ex); Shutdown(1); }
    }

    private void ReportFailure(Exception exception)
    {
        if (Interlocked.Exchange(ref reportedFailure, 1) != 0) return;
        WriteLog(exception.ToString());
        if (!smokeTest)
            MessageBox.Show("程序遇到错误，无法继续运行。\n\n" + exception.GetBaseException().Message + "\n\n错误详情已尝试保存到：\n" + LogPath + "\n\n请将错误详情提供给开发者。", "清简 DiskLens · 程序错误", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void WriteLog(string text, bool reset = false)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (reset) File.WriteAllText(LogPath, "");
            File.AppendAllText(LogPath, DateTimeOffset.Now.ToString("O") + " " + text + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { /* Diagnostics must not prevent startup. */ }
    }
}
