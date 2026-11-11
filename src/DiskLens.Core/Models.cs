namespace DiskLens.Core;

public static class SizeFormatter
{
    public static string Format(long size)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = Math.Max(0, size);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{value:N0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }
}

public sealed record FileSnapshot(string FullPath, long Length, DateTime LastWriteUtc, FileAttributes Attributes)
{
    public string Name => Path.GetFileName(FullPath);
    public string SizeText => SizeFormatter.Format(Length);
    public string ModifiedText => LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string Extension => Path.GetExtension(FullPath).ToLowerInvariant();
}

public sealed class DirectoryNode(string fullPath, DirectoryNode? parent = null)
{
    public string FullPath { get; } = fullPath;
    public string Name => Path.GetFileName(Path.TrimEndingDirectorySeparator(FullPath)) is { Length: > 0 } name ? name : FullPath;
    public DirectoryNode? Parent { get; } = parent;
    public List<DirectoryNode> Children { get; } = [];
    public long DirectBytes { get; internal set; }
    public long TotalBytes { get; internal set; }
    public long FileCount { get; internal set; }
    public string SizeText => SizeFormatter.Format(TotalBytes);
}

public sealed record FileCategory(string Name, long Bytes, long Count)
{
    public string SizeText => SizeFormatter.Format(Bytes);
}
public sealed record ScanIssue(string Path, string Reason);
public sealed record ScanProgress(long Files, long Bytes, int Directories, long Skipped, string CurrentPath);
public sealed record ScanResult(DirectoryNode Root, IReadOnlyList<FileSnapshot> LargestFiles,
    IReadOnlyList<FileSnapshot> TempCandidates, IReadOnlyList<FileCategory> Categories,
    IReadOnlyList<ScanIssue> Issues, long Skipped, bool Cancelled, bool LimitReached,
    DateTime StartedUtc, TimeSpan Duration)
{
    public bool IsPartial => Cancelled || LimitReached || Skipped > 0;
}

public sealed record CleanupDecision(bool Allowed, bool Suggested, string Reason);
public sealed record CleanupItem(FileSnapshot File, string Reason);
public sealed record CleanupOutcome(string Path, long Bytes, bool Success, string Message);
public sealed record CleanupReport(DateTime StartedUtc, IReadOnlyList<CleanupOutcome> Outcomes, bool Cancelled, string? JournalWarning = null)
{
    public long RecycledBytes => Outcomes.Where(x => x.Success).Sum(x => x.Bytes);
    public int SuccessCount => Outcomes.Count(x => x.Success);
}

public interface IFileRecycler
{
    // Must recycle or fail. Implementations must never fall back to permanent deletion.
    void Recycle(string fullPath);
}

public interface IOperationJournal
{
    void Begin(IReadOnlyList<CleanupItem> items);
    void Append(CleanupOutcome outcome);
}
