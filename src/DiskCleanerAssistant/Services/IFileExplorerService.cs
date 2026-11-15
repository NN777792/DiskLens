namespace DiskCleanerAssistant.Services;

public interface IFileExplorerService
{
    void SelectFile(string filePath);
    void OpenFolder(string folderPath);
}
