using DiskCleanerAssistant.Models;

namespace DiskCleanerAssistant.Services;

public interface IJunkScanner
{
    Task<JunkScanResult> ScanAsync(
        IProgress<JunkScanProgress>? progress,
        CancellationToken cancellationToken);
}
