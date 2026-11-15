using DiskCleanerAssistant.Models;

namespace DiskCleanerAssistant.Services;

public sealed class DriveService : IDriveService
{
    public IReadOnlyList<DriveItem> GetFixedDrives()
    {
        var drives = new List<DriveItem>();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            try
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "本地磁盘" : drive.VolumeLabel;
                drives.Add(new DriveItem
                {
                    Name = drive.Name,
                    DisplayName = $"{label} ({drive.Name.TrimEnd('\\')})",
                    Format = drive.DriveFormat,
                    TotalSize = drive.TotalSize,
                    FreeSpace = drive.AvailableFreeSpace
                });
            }
            catch (IOException)
            {
                // The drive may become unavailable while Windows is enumerating it.
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore protected volumes that cannot be queried by the current user.
            }
        }

        return drives.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
