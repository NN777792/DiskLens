using System.ComponentModel;
using System.Windows;
using DiskCleanerAssistant.Services;
using DiskCleanerAssistant.ViewModels;

namespace DiskCleanerAssistant;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(
            new DriveService(),
            new DirectoryScanner(),
            new RecycleBinService(),
            new DialogService(),
            new FileExplorerService());
        DataContext = _viewModel;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _viewModel.Stop();
    }
}
