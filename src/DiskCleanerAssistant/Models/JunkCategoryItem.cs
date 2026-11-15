using DiskCleanerAssistant.Utilities;
using DiskCleanerAssistant.ViewModels;

namespace DiskCleanerAssistant.Models;

public sealed class JunkCategoryItem : ViewModelBase
{
    private readonly List<JunkFileItem> _files = [];
    private bool _isSelected;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required bool IsRecommended { get; init; }
    public string SafetyText => IsRecommended ? "推荐清理" : "建议确认";
    public IReadOnlyList<JunkFileItem> Files => _files;
    public int FileCount => _files.Count;
    public long Size => _files.Sum(file => file.Size);
    public string SizeText => ByteSizeFormatter.Format(Size);
    public string FileCountText => $"{FileCount:N0} 个文件";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void SetFiles(IEnumerable<JunkFileItem> files)
    {
        _files.Clear();
        _files.AddRange(files);
        IsSelected = IsRecommended && _files.Count > 0;
        NotifyFileSummaryChanged();
    }

    public void RemoveFiles(ISet<string> deletedPaths)
    {
        _files.RemoveAll(file => deletedPaths.Contains(file.FullPath));
        if (_files.Count == 0)
        {
            IsSelected = false;
        }

        NotifyFileSummaryChanged();
    }

    private void NotifyFileSummaryChanged()
    {
        OnPropertyChanged(nameof(Files));
        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(Size));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(FileCountText));
    }
}
