using System.Collections.ObjectModel;
using System.ComponentModel;
using DiskCleanerAssistant.Commands;
using DiskCleanerAssistant.Models;
using DiskCleanerAssistant.Services;
using DiskCleanerAssistant.Utilities;

namespace DiskCleanerAssistant.ViewModels;

public sealed class JunkCleanupViewModel : ViewModelBase
{
    private readonly IJunkScanner _junkScanner;
    private readonly IRecycleBinService _recycleBinService;
    private readonly IDialogService _dialogService;
    private CancellationTokenSource? _scanCancellation;
    private bool _isScanning;
    private bool _isCleaning;
    private string _statusText = "扫描常见临时文件和缓存，确认后再清理";
    private string _currentPath = string.Empty;
    private long _scannedFileCount;
    private long _scannedBytes;
    private int _skippedCount;
    private TimeSpan _elapsed;
    private int _selectedCategoryCount;
    private long _selectedFileCount;
    private long _selectedBytes;

    public JunkCleanupViewModel(
        IJunkScanner junkScanner,
        IRecycleBinService recycleBinService,
        IDialogService dialogService)
    {
        _junkScanner = junkScanner;
        _recycleBinService = recycleBinService;
        _dialogService = dialogService;

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        CancelScanCommand = new RelayCommand(CancelScan, () => IsScanning);
        SelectRecommendedCommand = new RelayCommand(SelectRecommended, () => !IsBusy && Categories.Count > 0);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => !IsBusy && SelectedCategoryCount > 0);
        CleanSelectedCommand = new AsyncRelayCommand(CleanSelectedAsync, () => !IsBusy && SelectedFileCount > 0);
    }

    public ObservableCollection<JunkCategoryItem> Categories { get; } = [];
    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand CancelScanCommand { get; }
    public RelayCommand SelectRecommendedCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public AsyncRelayCommand CleanSelectedCommand { get; }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                NotifyCommandStates();
            }
        }
    }

    public bool IsCleaning
    {
        get => _isCleaning;
        private set
        {
            if (SetProperty(ref _isCleaning, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                NotifyCommandStates();
            }
        }
    }

    public bool IsBusy => IsScanning || IsCleaning;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string CurrentPath
    {
        get => _currentPath;
        private set => SetProperty(ref _currentPath, value);
    }

    public long ScannedFileCount
    {
        get => _scannedFileCount;
        private set
        {
            if (SetProperty(ref _scannedFileCount, value))
            {
                OnPropertyChanged(nameof(ScannedFileCountText));
            }
        }
    }

    public long ScannedBytes
    {
        get => _scannedBytes;
        private set
        {
            if (SetProperty(ref _scannedBytes, value))
            {
                OnPropertyChanged(nameof(ScannedBytesText));
            }
        }
    }

    public int SkippedCount
    {
        get => _skippedCount;
        private set
        {
            if (SetProperty(ref _skippedCount, value))
            {
                OnPropertyChanged(nameof(SkippedCountText));
            }
        }
    }

    public TimeSpan Elapsed
    {
        get => _elapsed;
        private set
        {
            if (SetProperty(ref _elapsed, value))
            {
                OnPropertyChanged(nameof(ElapsedText));
            }
        }
    }

    public int SelectedCategoryCount
    {
        get => _selectedCategoryCount;
        private set
        {
            if (SetProperty(ref _selectedCategoryCount, value))
            {
                OnPropertyChanged(nameof(SelectedSummaryText));
                NotifyCommandStates();
            }
        }
    }

    public long SelectedFileCount
    {
        get => _selectedFileCount;
        private set
        {
            if (SetProperty(ref _selectedFileCount, value))
            {
                OnPropertyChanged(nameof(SelectedSummaryText));
                NotifyCommandStates();
            }
        }
    }

    public long SelectedBytes
    {
        get => _selectedBytes;
        private set
        {
            if (SetProperty(ref _selectedBytes, value))
            {
                OnPropertyChanged(nameof(SelectedSummaryText));
            }
        }
    }

    public string ScannedFileCountText => $"{ScannedFileCount:N0} 个候选文件";
    public string ScannedBytesText => ByteSizeFormatter.Format(ScannedBytes);
    public string SkippedCountText => SkippedCount == 0 ? "无跳过项" : $"跳过 {SkippedCount:N0} 项";
    public string ElapsedText => Elapsed == TimeSpan.Zero ? "--" : $"{Elapsed.TotalSeconds:0.0} 秒";
    public string SelectedSummaryText =>
        $"已选择 {SelectedCategoryCount:N0} 类、{SelectedFileCount:N0} 个文件，共 {ByteSizeFormatter.Format(SelectedBytes)}";

    public void Stop() => CancelScan();

    private async Task ScanAsync()
    {
        ClearCategories();
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        var cancellation = _scanCancellation;
        IsScanning = true;
        StatusText = "正在扫描临时文件和缓存…";

        var progress = new Progress<JunkScanProgress>(value =>
        {
            CurrentPath = value.CurrentPath;
            ScannedFileCount = value.FileCount;
            ScannedBytes = value.TotalBytes;
            SkippedCount = value.SkippedCount;
            StatusText = $"正在扫描：{value.CategoryName}";
        });

        try
        {
            var result = await _junkScanner.ScanAsync(progress, cancellation.Token);
            foreach (var category in result.Categories)
            {
                category.PropertyChanged += CategoryOnPropertyChanged;
                Categories.Add(category);
            }

            ScannedFileCount = result.FileCount;
            ScannedBytes = result.TotalBytes;
            SkippedCount = result.SkippedCount;
            Elapsed = result.Elapsed;
            CurrentPath = string.Empty;
            RecalculateSelection();
            StatusText = result.FileCount == 0
                ? "扫描完成，没有发现可清理的临时文件"
                : $"扫描完成，发现 {result.FileCount:N0} 个候选文件";
        }
        catch (OperationCanceledException)
        {
            StatusText = "垃圾扫描已取消";
            CurrentPath = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText = "垃圾扫描失败";
            CurrentPath = string.Empty;
            _dialogService.ShowError("垃圾扫描失败", ex.Message);
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void CancelScan()
    {
        if (IsScanning)
        {
            StatusText = "正在停止扫描…";
            _scanCancellation?.Cancel();
        }
    }

    private void SelectRecommended()
    {
        foreach (var category in Categories)
        {
            category.IsSelected = category.IsRecommended && category.FileCount > 0;
        }
    }

    private void ClearSelection()
    {
        foreach (var category in Categories)
        {
            category.IsSelected = false;
        }
    }

    private async Task CleanSelectedAsync()
    {
        var selectedCategories = Categories
            .Where(category => category.IsSelected && category.FileCount > 0)
            .ToList();
        var selectedFiles = selectedCategories
            .SelectMany(category => category.Files)
            .DistinctBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selectedFiles.Count == 0)
        {
            return;
        }

        var categoryNames = string.Join("、", selectedCategories.Select(category => category.Name));
        var confirmed = _dialogService.Confirm(
            "清理垃圾文件",
            $"确定清理以下项目吗？\n\n{categoryNames}\n\n" +
            $"共 {selectedFiles.Count:N0} 个文件，预计释放 {ByteSizeFormatter.Format(selectedFiles.Sum(file => file.Size))}。\n" +
            "文件将移入 Windows 回收站，正在使用或无权限的文件会自动跳过。 ");
        if (!confirmed)
        {
            return;
        }

        IsCleaning = true;
        StatusText = "正在把垃圾文件移入回收站…";
        try
        {
            var result = await _recycleBinService.MoveToRecycleBinAsync(
                selectedFiles.Select(file => file.FullPath).ToArray());
            var deletedPaths = result.DeletedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var deletedBytes = selectedFiles
                .Where(file => deletedPaths.Contains(file.FullPath))
                .Sum(file => file.Size);

            foreach (var category in Categories)
            {
                category.RemoveFiles(deletedPaths);
            }

            ScannedFileCount = Categories.Sum(category => (long)category.FileCount);
            ScannedBytes = Categories.Sum(category => category.Size);
            RecalculateSelection();
            StatusText = result.Failures.Count == 0
                ? $"清理完成，已释放 {ByteSizeFormatter.Format(deletedBytes)}"
                : $"已释放 {ByteSizeFormatter.Format(deletedBytes)}，{result.Failures.Count:N0} 个文件被占用或无权限";

            if (result.Failures.Count > 0)
            {
                var examples = string.Join(
                    Environment.NewLine,
                    result.Failures.Take(5).Select(failure => $"• {failure.Path}：{failure.Message}"));
                _dialogService.ShowInfo(
                    "清理完成",
                    $"成功清理 {result.DeletedPaths.Count:N0} 个文件。\n" +
                    $"另有 {result.Failures.Count:N0} 个文件被跳过。\n\n{examples}");
            }
        }
        catch (Exception ex)
        {
            StatusText = "垃圾清理失败";
            _dialogService.ShowError("垃圾清理失败", ex.Message);
        }
        finally
        {
            IsCleaning = false;
        }
    }

    private void ClearCategories()
    {
        foreach (var category in Categories)
        {
            category.PropertyChanged -= CategoryOnPropertyChanged;
        }

        Categories.Clear();
        ScannedFileCount = 0;
        ScannedBytes = 0;
        SkippedCount = 0;
        Elapsed = TimeSpan.Zero;
        CurrentPath = string.Empty;
        SelectedCategoryCount = 0;
        SelectedFileCount = 0;
        SelectedBytes = 0;
    }

    private void CategoryOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(JunkCategoryItem.IsSelected) ||
            e.PropertyName == nameof(JunkCategoryItem.Size))
        {
            RecalculateSelection();
        }
    }

    private void RecalculateSelection()
    {
        var selected = Categories.Where(category => category.IsSelected).ToList();
        SelectedCategoryCount = selected.Count;
        SelectedFileCount = selected.Sum(category => (long)category.FileCount);
        SelectedBytes = selected.Sum(category => category.Size);
    }

    private void NotifyCommandStates()
    {
        ScanCommand.NotifyCanExecuteChanged();
        CancelScanCommand.NotifyCanExecuteChanged();
        SelectRecommendedCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
        CleanSelectedCommand.NotifyCanExecuteChanged();
    }
}
