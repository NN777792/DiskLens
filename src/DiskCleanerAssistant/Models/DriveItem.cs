using DiskCleanerAssistant.Utilities;

namespace DiskCleanerAssistant.Models;

public sealed class DriveItem
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Format { get; init; }
    public long TotalSize { get; init; }
    public long FreeSpace { get; init; }
    public long UsedSpace => Math.Max(0, TotalSize - FreeSpace);
    public double UsagePercent => TotalSize == 0 ? 0 : UsedSpace * 100d / TotalSize;
    public string CapacityText => $"已用 {ByteSizeFormatter.Format(UsedSpace)} / {ByteSizeFormatter.Format(TotalSize)}";
    public string FreeText => $"可用 {ByteSizeFormatter.Format(FreeSpace)}";
}
