namespace DiskCleanerAssistant.Models;

public sealed record ScanProgress(string CurrentPath, long FileCount, long TotalBytes, int SkippedCount);
