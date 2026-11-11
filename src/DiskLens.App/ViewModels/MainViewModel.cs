using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DiskLens.App.Infrastructure;
using DiskLens.App.Services;
using DiskLens.Core;

namespace DiskLens.App.ViewModels;

public sealed record DriveCard(string Root, string Name, string Kind, long Total, long Free)
{
    public string TotalText => SizeFormatter.Format(Total);
    public string FreeText => SizeFormatter.Format(Free);
    public string UsedText => SizeFormatter.Format(Total - Free);
    public double Percent => Total > 0 ? (Total - Free) * 100d / Total : 0;
    public Brush BarBrush => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Percent > 90 ? "#E18552" : "#5979E8"));
}
public sealed record DirectoryRow(DirectoryNode Node, double Percent)
{
    public string Name => Node.Name;
    public string Path => Node.FullPath;
    public string Size => Node.SizeText;
    public long Bytes => Node.TotalBytes;
    public string Count => $"{Node.FileCount:N0} 个文件";
    public string Share => $"{Percent:0.0}%";
}
public sealed record CategoryRow(string Name, string Size, string Count, double Percent, Brush Color);

public sealed class FileRow(FileSnapshot file, CleanupDecision decision) : ObservableObject
{
    public FileSnapshot File { get; } = file;
    public string Name => File.Name;
    public string Path => File.FullPath;
    public long Bytes => File.Length;
    public string Size => File.SizeText;
    public string Modified => File.ModifiedText;
    public string Reason => decision.Reason;
    public string Kind => decision.Suggested ? "临时文件" : decision.Allowed ? "个人文件" : "受保护";
    public bool CanSelect => decision.Allowed;
    public bool Suggested => decision.Suggested;
    private bool selected;
    public bool Selected { get => selected; set => Set(ref selected, CanSelect && value); }
}

public sealed class MainViewModel : ObservableObject
{
    public CleanupPolicy Policy { get; } = CleanupPolicy.ForCurrentUser();
    public ObservableCollection<DriveCard> Drives { get; } = [];
    public ObservableCollection<DirectoryRow> Directories { get; } = [];
    public ObservableCollection<FileRow> Files { get; } = [];
    public ObservableCollection<FileRow> TempFiles { get; } = [];
    public ObservableCollection<CategoryRow> Categories { get; } = [];
    public ObservableCollection<HistoryEntry> History { get; } = [];
    public ICollectionView FilteredFiles { get; }
    public IReadOnlyList<FileRow> AllRows => rows;
    private List<FileRow> rows = [];
    public ScanResult? Result { get; private set; }
    private DirectoryNode? currentNode;
    public DirectoryNode? CurrentNode { get => currentNode; private set { Set(ref currentNode, value); Raise(nameof(CurrentPath)); Raise(nameof(CanGoUp)); } }
    public string CurrentPath => CurrentNode?.FullPath ?? "选择磁盘或文件夹，开始了解你的空间";
    public bool CanGoUp => CurrentNode?.Parent != null && !IsBusy;
    private bool isBusy;
    public bool IsBusy { get => isBusy; set { Set(ref isBusy, value); Raise(nameof(IsIdle)); Raise(nameof(CanGoUp)); CommandManager.InvalidateRequerySuggested(); } }
    public bool IsIdle => !IsBusy;
    private bool scanning;
    public bool IsScanning { get => scanning; set => Set(ref scanning, value); }
    private string status = "就绪 · 扫描只读取文件信息";
    public string Status { get => status; set => Set(ref status, value); }
    private string scanPath = "";
    public string ScanPath { get => scanPath; set => Set(ref scanPath, value); }
    private string scanSize = "—";
    public string ScanSize { get => scanSize; set => Set(ref scanSize, value); }
    private string scanCount = "尚未扫描";
    public string ScanCount { get => scanCount; set => Set(ref scanCount, value); }
    private string candidateSize = "—";
    public string CandidateSize { get => candidateSize; set => Set(ref candidateSize, value); }
    private string duration = "—";
    public string Duration { get => duration; set => Set(ref duration, value); }
    private string notice = "扫描后可以点击空间图中的文件夹下钻；大文件仅用于分析，请确认用途后再清理。";
    public string Notice { get => notice; set => Set(ref notice, value); }
    private string search = "";
    public string Search { get => search; set { Set(ref search, value); FilteredFiles.Refresh(); Raise(nameof(FileListCaption)); } }
    private int sizeFilter;
    public int SizeFilter { get => sizeFilter; set { Set(ref sizeFilter, value); FilteredFiles.Refresh(); Raise(nameof(FileListCaption)); } }
    public string FileListCaption => $"保留最大的 {Files.Count:N0} 个文件；筛选作用于此列表。大小为逻辑字节，硬链接可能重复计数。";
    public string SelectedText => $"已选 {rows.Count(x => x.Selected):N0} 项 · {SizeFormatter.Format(rows.Where(x => x.Selected).Sum(x => x.Bytes))}";
    public bool HasSelected => rows.Any(x => x.Selected) && !IsBusy;
    public string TempCaption => $"本次找到 {TempFiles.Count:N0} 个建议项（最多保留 {DiskScanner.TempCandidateLimit:N0} 项），全部默认不勾选。";
    public ICommand UpCommand { get; }
    public ICommand RootCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand ClearSelectionCommand { get; }

    public MainViewModel()
    {
        FilteredFiles = CollectionViewSource.GetDefaultView(Files);
        FilteredFiles.Filter = value => value is FileRow row && (string.IsNullOrWhiteSpace(Search) || row.Path.Contains(Search, StringComparison.OrdinalIgnoreCase))
            && row.Bytes >= (SizeFilter switch { 1 => 100L * 1024 * 1024, 2 => 1024L * 1024 * 1024, 3 => 10L * 1024 * 1024 * 1024, _ => 0 });
        UpCommand = new RelayCommand(_ => Navigate(CurrentNode!.Parent!), _ => CanGoUp);
        RootCommand = new RelayCommand(_ => Navigate(Result!.Root), _ => Result != null && !IsBusy);
        NavigateCommand = new RelayCommand(value => { if (value is DirectoryNode node) Navigate(node); }, _ => !IsBusy);
        ClearSelectionCommand = new RelayCommand(_ => { foreach (var row in rows) row.Selected = false; }, _ => !IsBusy);
        RefreshDrives();
        try { RefreshHistory(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "历史记录暂不可读：" + ex.Message; }
    }

    public void RefreshDrives()
    {
        Drives.Clear();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "本地磁盘" : drive.VolumeLabel;
                Drives.Add(new DriveCard(drive.RootDirectory.FullName, $"{label} ({drive.Name.TrimEnd('\\')})", drive.DriveType == DriveType.Fixed ? drive.DriveFormat + " · 本地磁盘" : "可移动磁盘 · 仅分析", drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Disconnected media. */ }
        }
    }

    public void BeginScan(string path)
    {
        // A new scan invalidates all selections and previous results immediately.
        Result = null; CurrentNode = null; rows = []; Files.Clear(); TempFiles.Clear(); Directories.Clear(); Categories.Clear();
        ScanPath = path; ScanSize = "0 B"; ScanCount = "准备扫描…"; CandidateSize = "—"; Duration = "扫描中";
        Notice = "正在扫描。空间总量为已读取文件的逻辑大小；不可访问的目录会跳过。";
        SelectionChanged(); IsBusy = true; IsScanning = true;
    }

    public void AcceptProgress(ScanProgress progress)
    {
        ScanSize = SizeFormatter.Format(progress.Bytes);
        ScanCount = $"{progress.Files:N0} 个文件 · {progress.Directories:N0} 个目录";
        Status = $"正在扫描 · 跳过 {progress.Skipped:N0} 项";
        ScanPath = progress.CurrentPath;
    }

    public void Load(ScanResult result)
    {
        Result = result;
        var unique = new Dictionary<string, FileRow>(PathSafety.Comparer);
        var localFixed = false;
        try { localFixed = new DriveInfo(Path.GetPathRoot(result.Root.FullPath)!).DriveType == DriveType.Fixed; }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
        FileRow Row(FileSnapshot file)
        {
            if (unique.TryGetValue(file.FullPath, out var existing)) return existing;
            var decision = localFixed ? Policy.Evaluate(file) : new CleanupDecision(false, false, "此位置仅支持分析；清理只支持本地固定磁盘");
            var row = new FileRow(file, decision);
            row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FileRow.Selected)) SelectionChanged(); };
            unique.Add(file.FullPath, row); return row;
        }
        foreach (var file in result.LargestFiles) Files.Add(Row(file));
        foreach (var file in result.TempCandidates) TempFiles.Add(Row(file));
        rows = unique.Values.ToList();
        ScanSize = result.Root.SizeText;
        ScanCount = $"{result.Root.FileCount:N0} 个文件 · {result.Skipped:N0} 项跳过";
        CandidateSize = SizeFormatter.Format(result.TempCandidates.Sum(x => x.Length));
        Duration = result.Duration.TotalSeconds < 60 ? $"{result.Duration.TotalSeconds:0.0} 秒" : $"{result.Duration.TotalMinutes:0.0} 分钟";
        string[] colors = ["#5979E8", "#58AEAA", "#A18AD4", "#EDB76A", "#E58C98", "#78A9D9", "#ACB5C9"];
        var index = 0;
        foreach (var category in result.Categories)
            Categories.Add(new(category.Name, category.SizeText, $"{category.Count:N0} 项", result.Root.TotalBytes > 0 ? category.Bytes * 100d / result.Root.TotalBytes : 0, new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[index++ % colors.Length]))));
        IsScanning = false; IsBusy = false;
        Status = result.Cancelled ? "扫描已取消 · 展示部分结果" : result.IsPartial ? "扫描结束 · 存在跳过项目" : "扫描完成";
        Notice = result.Cancelled ? "扫描被取消，当前为部分结果。重新扫描可获得更完整的分析。" : result.LimitReached ? "目录数量达到 250,000 上限，当前为部分结果。请选择更小的目录继续扫描。" : result.Skipped > 0 ? $"有 {result.Skipped:N0} 个文件或目录被跳过（权限、链接或云盘占位等），详见扫描详情。" : "扫描完成。图表反映文件逻辑大小；稀疏文件、压缩、硬链接及系统保留空间会让它与磁盘已用空间不同。";
        Navigate(result.Root); SelectionChanged();
        Raise(nameof(FileListCaption)); Raise(nameof(TempCaption));
    }

    public void Navigate(DirectoryNode node)
    {
        CurrentNode = node; Directories.Clear();
        foreach (var child in node.Children.OrderByDescending(x => x.TotalBytes).Take(1000))
            Directories.Add(new(child, node.TotalBytes > 0 ? child.TotalBytes * 100d / node.TotalBytes : 0));
        Raise(nameof(DirectoryCaption));
    }
    public string DirectoryCaption => CurrentNode == null ? "扫描后查看目录占用" : $"本层 {CurrentNode.Children.Count:N0} 个子目录（最多显示前 1,000）；本层文件 {SizeFormatter.Format(CurrentNode.DirectBytes)}";
    public void SelectionChanged() { Raise(nameof(SelectedText)); Raise(nameof(HasSelected)); CommandManager.InvalidateRequerySuggested(); }
    public void RefreshHistory() { History.Clear(); foreach (var item in JsonJournal.Read()) History.Add(item); }
    public void MarkRecycled(CleanupReport report)
    {
        var paths = report.Outcomes.Where(x => x.Success).Select(x => x.Path).ToHashSet(PathSafety.Comparer);
        foreach (var row in rows.Where(x => paths.Contains(x.Path)).ToArray()) { row.Selected = false; Files.Remove(row); TempFiles.Remove(row); rows.Remove(row); }
        CandidateSize = SizeFormatter.Format(TempFiles.Sum(x => x.Bytes));
        Notice = "文件已移入回收站，磁盘可用空间通常不会立即增加；可在 Windows 回收站还原。空间图是扫描快照，请重新扫描更新。";
        RefreshDrives(); RefreshHistory(); SelectionChanged(); Raise(nameof(FileListCaption)); Raise(nameof(TempCaption));
    }
}
