using DiskCleanerAssistant.Models;

namespace DiskCleanerAssistant.Services;

public interface IDirectoryScanner
{
    Task<ScanResult> ScanAsync(
        string rootPath,
        long minimumFileSize,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);
}
