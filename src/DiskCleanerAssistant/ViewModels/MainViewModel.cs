using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using DiskCleanerAssistant.Commands;
using DiskCleanerAssistant.Models;
using DiskCleanerAssistant.Services;
using DiskCleanerAssistant.Utilities;

namespace DiskCleanerAssistant.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IDriveService _driveService;
    private readonly IDirectoryScanner _directoryScanner;
    private readonly IRecycleBinService _recycleBinService;
    private readonly IDialogService _dialogService;
    private readonly IFileExplorerService _fileExplorerService;
    private CancellationTokenSource? _scanCancellation;
    private string _scanPath = string.Empty;
    private SizeOption? _selectedSizeOption;
    private string _selectedFileType = "全部文件";
    private string _searchText = string.Empty;
    private bool _isScanning;
    private bool _isCleaning;
    private string _statusText = "请选择磁盘或文件夹，然后开始扫描";
    private string _currentPath = string.Empty;
    private long _scannedFileCount;
    private long _scannedBytes;
    private int _skippedCount;
    private TimeSpan _elapsed;
    private int _selectedCount;
    private long _selectedBytes;

    public MainViewModel(
        IDriveService driveService,
        IDirectoryScanner directoryScanner,
        IRecycleBinService recycleBinService,
        IDialogService dialogService,
        IFileExplorerService fileExplorerService)
    {
        _driveService = driveService;
        _directoryScanner = directoryScanner;
        _recycleBinService = recycleBinService;
        _dialogService = dialogService;
        _fileExplorerService = fileExplorerService;

        SizeOptions =
        [
            new SizeOption("100 MB 以上", 100L * 1024 * 1024),
            new SizeOption("500 MB 以上", 500L * 1024 * 1024),
            new SizeOption("1 GB 以上", 1024L * 1024 * 1024),
            new SizeOption("2 GB 以上", 2L * 1024 * 1024 * 1024),
            new SizeOption("5 GB 以上", 5L * 1024 * 1024 * 1024)
        ];
        _selectedSizeOption = SizeOptions[0];

        FileTypes = ["全部文件", "视频", "压缩包", "安装包", "图片", "文档", "其他"];
        FilteredFiles = CollectionViewSource.GetDefaultView(LargeFiles);
        FilteredFiles.Filter = FilterFile;

        RefreshDrivesCommand = new RelayCommand(RefreshDrives, () => !IsScanning);
        SelectDriveCommand = new RelayCommand(SelectDrive, parameter => parameter is DriveItem && !IsScanning);
        BrowseCommand = new RelayCommand(Browse, () => !IsScanning);
        StartScanCommand = new AsyncRelayCommand(StartScanAsync, CanStartScan);
        CancelScanCommand = new RelayCommand(CancelScan, () => IsScanning);
        SelectAllVisibleCommand = new RelayCommand(SelectAllVisible, () => !IsScanning && LargeFiles.Count > 0);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => SelectedCount > 0);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, CanDeleteSelected);
        OpenFileLocationCommand = new RelayCommand(OpenFileLocation, parameter => parameter is FileItem);
        OpenFolderCommand = new RelayCommand(OpenFolder, parameter => parameter is FolderUsageItem);

        RefreshDrives();
    }

    public ObservableCollection<DriveItem> Drives { get; } = [];
    public ObservableCollection<FolderUsageItem> FolderUsages { get; } = [];
    public ObservableCollection<FileItem> LargeFiles { get; } = [];
    public IReadOnlyList<SizeOption> SizeOptions { get; }
    public IReadOnlyList<string> FileTypes { get; }
    public ICollectionView FilteredFiles { get; }

    public RelayCommand RefreshDrivesCommand { get; }
    public RelayCommand SelectDriveCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public AsyncRelayCommand StartScanCommand { get; }
    public RelayCommand CancelScanCommand { get; }
    public RelayCommand SelectAllVisibleCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public AsyncRelayCommand DeleteSelectedCommand { get; }
    public RelayCommand OpenFileLocationCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    public string ScanPath
    {
        get => _scanPath;
        set
        {
            if (SetProperty(ref _scanPath, value))
            {
                StartScanCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public SizeOption? SelectedSizeOption
    {
        get => _selectedSizeOption;
        set => SetProperty(ref _selectedSizeOption, value);
    }

    public string SelectedFileType
    {
        get => _selectedFileType;
        set
        {
            if (SetProperty(ref _selectedFileType, value))
            {
                FilteredFiles.Refresh();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                FilteredFiles.Refresh();
            }
        }
    }

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

    public int SelectedCount
    {
        get => _selectedCount;
        private set
        {
            if (SetProperty(ref _selectedCount, value))
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

    public string ScannedFileCountText => $"{ScannedFileCount:N0} 个文件";
    public string ScannedBytesText => ByteSizeFormatter.Format(ScannedBytes);
    public string SkippedCountText => SkippedCount == 0 ? "无跳过项" : $"跳过 {SkippedCount:N0} 项";
    public string ElapsedText => Elapsed == TimeSpan.Zero ? "--" : $"{Elapsed.TotalSeconds:0.0} 秒";
    public string SelectedSummaryText => $"已选择 {SelectedCount:N0} 个文件，共 {ByteSizeFormatter.Format(SelectedBytes)}";

    public void Stop() => CancelScan();

    private void RefreshDrives()
    {
        var previousPath = ScanPath;
        Drives.Clear();
        foreach (var drive in _driveService.GetFixedDrives())
        {
            Drives.Add(drive);
        }

        if (string.IsNullOrWhiteSpace(previousPath) && Drives.Count > 0)
        {
            ScanPath = Drives[0].Name;
        }
    }

    private void SelectDrive(object? parameter)
    {
        if (parameter is DriveItem drive)
        {
            ScanPath = drive.Name;
        }
    }

    private void Browse()
    {
        var selected = _dialogService.ChooseFolder(ScanPath);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            ScanPath = selected;
        }
    }

    private bool CanStartScan() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(ScanPath) &&
        Directory.Exists(ScanPath) &&
        SelectedSizeOption is not null;

    private async Task StartScanAsync()
    {
        var selectedSizeOption = SelectedSizeOption;
        if (!Directory.Exists(ScanPath) || selectedSizeOption is null)
        {
            _dialogService.ShowError("无法扫描", "请选择一个存在的磁盘或文件夹。 ");
            return;
        }

        ClearResults();
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        var scanCancellation = _scanCancellation;
        IsScanning = true;
        StatusText = "正在分析磁盘空间…";
        CurrentPath = ScanPath;

        var progress = new Progress<ScanProgress>(value =>
        {
            CurrentPath = value.CurrentPath;
            ScannedFileCount = value.FileCount;
            ScannedBytes = value.TotalBytes;
            SkippedCount = value.SkippedCount;
        });

        try
        {
            var result = await _directoryScanner.ScanAsync(
                ScanPath,
                selectedSizeOption.Bytes,
                progress,
                scanCancellation.Token);

            foreach (var folder in result.Folders)
            {
                FolderUsages.Add(folder);
            }

            foreach (var file in result.LargeFiles)
            {
                file.PropertyChanged += FileItemOnPropertyChanged;
                LargeFiles.Add(file);
            }

            ScannedFileCount = result.FileCount;
            ScannedBytes = result.TotalBytes;
            SkippedCount = result.SkippedCount;
            Elapsed = result.Elapsed;
            CurrentPath = string.Empty;
            StatusText = result.SkippedCount == 0
                ? $"扫描完成，找到 {result.LargeFiles.Count:N0} 个大文件"
                : $"扫描完成，找到 {result.LargeFiles.Count:N0} 个大文件；部分受保护项目已跳过";
            FilteredFiles.Refresh();
        }
        catch (OperationCanceledException)
        {
            StatusText = "扫描已取消";
            CurrentPath = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText = "扫描失败";
            CurrentPath = string.Empty;
            _dialogService.ShowError("扫描失败", ex.Message);
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

    private void ClearResults()
    {
        foreach (var file in LargeFiles)
        {
            file.PropertyChanged -= FileItemOnPropertyChanged;
        }

        FolderUsages.Clear();
        LargeFiles.Clear();
        ScannedFileCount = 0;
        ScannedBytes = 0;
        SkippedCount = 0;
        Elapsed = TimeSpan.Zero;
        SelectedCount = 0;
        SelectedBytes = 0;
    }

    private bool FilterFile(object item)
    {
        if (item is not FileItem file)
        {
            return false;
        }

        if (SelectedFileType != "全部文件" && file.Category != SelectedFileType)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return file.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               file.FullPath.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void SelectAllVisible()
    {
        foreach (var file in FilteredFiles.Cast<FileItem>().ToList())
        {
            file.IsSelected = true;
        }
    }

    private void ClearSelection()
    {
        foreach (var file in LargeFiles)
        {
            file.IsSelected = false;
        }
    }

    private bool CanDeleteSelected() => !IsBusy && SelectedCount > 0;

    private async Task DeleteSelectedAsync()
    {
        var selected = LargeFiles.Where(file => file.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var confirmed = _dialogService.Confirm(
            "移入回收站",
            $"确定把选中的 {selected.Count:N0} 个文件移入回收站吗？\n\n" +
            $"预计释放 {ByteSizeFormatter.Format(selected.Sum(file => file.Size))}。文件仍可从回收站恢复。 ");
        if (!confirmed)
        {
            return;
        }

        IsCleaning = true;
        StatusText = "正在把文件移入回收站…";
        try
        {
            var result = await _recycleBinService.MoveToRecycleBinAsync(
                selected.Select(file => file.FullPath).ToArray());

            var deletedSet = result.DeletedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var deletedItems = selected.Where(file => deletedSet.Contains(file.FullPath)).ToList();
            foreach (var file in deletedItems)
            {
                file.PropertyChanged -= FileItemOnPropertyChanged;
                LargeFiles.Remove(file);
                ApplyDeletionToSummary(file);
            }

            RecalculateSelection();
            RecalculateFolderPercentages();
            FilteredFiles.Refresh();
            RefreshDrives();

            StatusText = result.Failures.Count == 0
                ? $"已将 {deletedItems.Count:N0} 个文件移入回收站"
                : $"已清理 {deletedItems.Count:N0} 个文件，{result.Failures.Count:N0} 个文件处理失败";

            if (result.Failures.Count > 0)
            {
                var examples = string.Join(
                    Environment.NewLine,
                    result.Failures.Take(5).Select(failure => $"• {failure.Path}：{failure.Message}"));
                _dialogService.ShowError(
                    "部分文件未能清理",
                    $"有 {result.Failures.Count:N0} 个文件未能移入回收站。\n\n{examples}");
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "清理已取消";
        }
        catch (Exception ex)
        {
            StatusText = "清理失败";
            _dialogService.ShowError("清理失败", ex.Message);
        }
        finally
        {
            IsCleaning = false;
        }
    }

    private void ApplyDeletionToSummary(FileItem file)
    {
        ScannedBytes = Math.Max(0, ScannedBytes - file.Size);
        ScannedFileCount = Math.Max(0, ScannedFileCount - 1);

        var folder = FolderUsages.FirstOrDefault(item => FileBelongsToFolder(file, item));
        if (folder is not null)
        {
            folder.Size -= file.Size;
            folder.FileCount--;
        }
    }

    private bool FileBelongsToFolder(FileItem file, FolderUsageItem folder)
    {
        if (PathsEqual(folder.FullPath, ScanPath))
        {
            return PathsEqual(file.DirectoryPath, ScanPath);
        }

        var relative = Path.GetRelativePath(folder.FullPath, file.FullPath);
        return !Path.IsPathRooted(relative) &&
               relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private void RecalculateFolderPercentages()
    {
        foreach (var folder in FolderUsages)
        {
            folder.PercentOfTotal = ScannedBytes == 0 ? 0 : folder.Size * 100d / ScannedBytes;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);

    private void OpenFileLocation(object? parameter)
    {
        if (parameter is not FileItem file)
        {
            return;
        }

        try
        {
            _fileExplorerService.SelectFile(file.FullPath);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("无法打开文件位置", ex.Message);
        }
    }

    private void OpenFolder(object? parameter)
    {
        if (parameter is not FolderUsageItem folder)
        {
            return;
        }

        try
        {
            _fileExplorerService.OpenFolder(folder.FullPath);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("无法打开文件夹", ex.Message);
        }
    }

    private void FileItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileItem.IsSelected))
        {
            RecalculateSelection();
        }
    }

    private void RecalculateSelection()
    {
        var selected = LargeFiles.Where(file => file.IsSelected).ToList();
        SelectedCount = selected.Count;
        SelectedBytes = selected.Sum(file => file.Size);
    }

    private void NotifyCommandStates()
    {
        RefreshDrivesCommand.NotifyCanExecuteChanged();
        SelectDriveCommand.NotifyCanExecuteChanged();
        BrowseCommand.NotifyCanExecuteChanged();
        StartScanCommand.NotifyCanExecuteChanged();
        CancelScanCommand.NotifyCanExecuteChanged();
        SelectAllVisibleCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }
}
