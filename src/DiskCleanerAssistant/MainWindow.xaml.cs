using System.ComponentModel;
using System.Windows;
using DiskCleanerAssistant.Services;
using DiskCleanerAssistant.Utilities;
using DiskCleanerAssistant.ViewModels;

namespace DiskCleanerAssistant;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        StartupDiagnostics.Mark("MainWindow constructor started");
        InitializeComponent();
        StartupDiagnostics.Mark("MainWindow XAML initialized");
        _viewModel = new MainViewModel(
            new DriveService(),
            new DirectoryScanner(),
            new RecycleBinService(),
            new DialogService(),
            new FileExplorerService());
        StartupDiagnostics.Mark("MainViewModel created");
        DataContext = _viewModel;
        Loaded += (_, _) => StartupDiagnostics.Mark("MainWindow loaded");
        ContentRendered += (_, _) => StartupDiagnostics.Mark("MainWindow content rendered");
        StartupDiagnostics.Mark("MainWindow constructor completed");
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _viewModel.Stop();
    }
}
