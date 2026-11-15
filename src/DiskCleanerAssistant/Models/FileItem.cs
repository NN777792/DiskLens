using DiskCleanerAssistant.Utilities;
using DiskCleanerAssistant.ViewModels;

namespace DiskCleanerAssistant.Models;

public sealed class FileItem : ViewModelBase
{
    private bool _isSelected;

    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required string DirectoryPath { get; init; }
    public required string Extension { get; init; }
    public required string Category { get; init; }
    public long Size { get; init; }
    public DateTime LastModified { get; init; }
    public string SizeText => ByteSizeFormatter.Format(Size);
    public string LastModifiedText => LastModified.ToString("yyyy-MM-dd HH:mm");

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
