using DiskCleanerAssistant.Models;

namespace DiskCleanerAssistant.Services;

public interface IDriveService
{
    IReadOnlyList<DriveItem> GetFixedDrives();
}
