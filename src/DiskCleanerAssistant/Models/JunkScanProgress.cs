namespace DiskCleanerAssistant.Models;

public sealed record JunkScanProgress(
    string CategoryName,
    string CurrentPath,
    long FileCount,
    long TotalBytes,
    int SkippedCount);
