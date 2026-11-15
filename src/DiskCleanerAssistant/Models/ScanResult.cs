namespace DiskCleanerAssistant.Models;

public sealed class ScanResult
{
    public required IReadOnlyList<FolderUsageItem> Folders { get; init; }
    public required IReadOnlyList<FileItem> LargeFiles { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public long TotalBytes { get; init; }
    public long FileCount { get; init; }
    public int SkippedCount { get; init; }
    public TimeSpan Elapsed { get; init; }
}
