namespace DiskCleanerAssistant.Models;

public sealed class JunkScanResult
{
    public required IReadOnlyList<JunkCategoryItem> Categories { get; init; }
    public long FileCount { get; init; }
    public long TotalBytes { get; init; }
    public int SkippedCount { get; init; }
    public TimeSpan Elapsed { get; init; }
}
