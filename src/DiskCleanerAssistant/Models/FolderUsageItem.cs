using DiskCleanerAssistant.Utilities;
using DiskCleanerAssistant.ViewModels;

namespace DiskCleanerAssistant.Models;

public sealed class FolderUsageItem : ViewModelBase
{
    private long _size;
    private long _fileCount;
    private double _percentOfTotal;

    public required string Name { get; init; }
    public required string FullPath { get; init; }

    public long Size
    {
        get => _size;
        set
        {
            if (SetProperty(ref _size, Math.Max(0, value)))
            {
                OnPropertyChanged(nameof(SizeText));
            }
        }
    }

    public long FileCount
    {
        get => _fileCount;
        set => SetProperty(ref _fileCount, Math.Max(0, value));
    }

    public double PercentOfTotal
    {
        get => _percentOfTotal;
        set => SetProperty(ref _percentOfTotal, Math.Clamp(value, 0, 100));
    }

    public string SizeText => ByteSizeFormatter.Format(Size);
}
