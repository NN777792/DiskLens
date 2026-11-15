using System.Diagnostics;

namespace DiskCleanerAssistant.Services;

public sealed class FileExplorerService : IFileExplorerService
{
    public void SelectFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("文件已经不存在。", filePath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{filePath}\"",
            UseShellExecute = true
        });
    }

    public void OpenFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"目录已经不存在：{folderPath}");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = folderPath,
            UseShellExecute = true
        });
    }
}
