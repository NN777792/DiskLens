using System.Diagnostics;
using DiskCleanerAssistant.Models;

namespace DiskCleanerAssistant.Services;

public sealed class DirectoryScanner : IDirectoryScanner
{
    private static readonly System.IO.EnumerationOptions ScanEnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts"
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso"
    };

    private static readonly HashSet<string> InstallerExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msix", ".appx", ".cab"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".raw"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv", ".md"
    };

    public Task<ScanResult> ScanAsync(
        string rootPath,
        long minimumFileSize,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => Scan(rootPath, minimumFileSize, progress, cancellationToken),
            cancellationToken);
    }

    private static ScanResult Scan(
        string rootPath,
        long minimumFileSize,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var normalizedRoot = Path.GetFullPath(rootPath);
        if (!Directory.Exists(normalizedRoot))
        {
            throw new DirectoryNotFoundException($"找不到目录：{normalizedRoot}");
        }

        var stopwatch = Stopwatch.StartNew();
        var lastProgressAt = TimeSpan.Zero;
        var largeFiles = new List<FileItem>();
        var errors = new List<string>();
        var buckets = new Dictionary<string, FolderAccumulator>(StringComparer.OrdinalIgnoreCase);
        var work = new Stack<DirectoryWorkItem>();
        work.Push(new DirectoryWorkItem(normalizedRoot, null));

        long totalBytes = 0;
        long fileCount = 0;
        var skippedCount = 0;

        while (work.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = work.Pop();

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(current.Path, "*", ScanEnumerationOptions);
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                skippedCount++;
                AddError(errors, current.Path, ex);
                continue;
            }

            try
            {
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entry);
                    }
                    catch (Exception ex) when (IsExpectedFileSystemException(ex))
                    {
                        skippedCount++;
                        AddError(errors, entry, ex);
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        skippedCount++;
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        var bucketPath = current.BucketPath;
                        if (PathsEqual(current.Path, normalizedRoot))
                        {
                            bucketPath = entry;
                        }

                        work.Push(new DirectoryWorkItem(entry, bucketPath));
                        continue;
                    }

                    try
                    {
                        var file = new FileInfo(entry);
                        var size = file.Length;
                        totalBytes += size;
                        fileCount++;

                        var fileBucketPath = current.BucketPath ?? normalizedRoot;
                        if (!buckets.TryGetValue(fileBucketPath, out var accumulator))
                        {
                            accumulator = new FolderAccumulator(fileBucketPath);
                            buckets.Add(fileBucketPath, accumulator);
                        }

                        accumulator.Size += size;
                        accumulator.FileCount++;

                        if (size >= minimumFileSize)
                        {
                            var extension = file.Extension;
                            largeFiles.Add(new FileItem
                            {
                                Name = file.Name,
                                FullPath = file.FullName,
                                DirectoryPath = file.DirectoryName ?? normalizedRoot,
                                Extension = extension,
                                Category = GetCategory(extension),
                                Size = size,
                                LastModified = file.LastWriteTime
                            });
                        }
                    }
                    catch (Exception ex) when (IsExpectedFileSystemException(ex))
                    {
                        skippedCount++;
                        AddError(errors, entry, ex);
                    }

                    if (stopwatch.Elapsed - lastProgressAt >= TimeSpan.FromMilliseconds(150))
                    {
                        progress?.Report(new ScanProgress(entry, fileCount, totalBytes, skippedCount));
                        lastProgressAt = stopwatch.Elapsed;
                    }
                }
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                skippedCount++;
                AddError(errors, current.Path, ex);
            }
        }

        stopwatch.Stop();
        progress?.Report(new ScanProgress(normalizedRoot, fileCount, totalBytes, skippedCount));

        var folders = buckets.Values
            .Select(bucket => new FolderUsageItem
            {
                Name = PathsEqual(bucket.Path, normalizedRoot)
                    ? "根目录文件"
                    : Path.GetFileName(Path.TrimEndingDirectorySeparator(bucket.Path)),
                FullPath = bucket.Path,
                Size = bucket.Size,
                FileCount = bucket.FileCount,
                PercentOfTotal = totalBytes == 0 ? 0 : bucket.Size * 100d / totalBytes
            })
            .OrderByDescending(folder => folder.Size)
            .ToList();

        return new ScanResult
        {
            Folders = folders,
            LargeFiles = largeFiles.OrderByDescending(file => file.Size).ToList(),
            Errors = errors,
            TotalBytes = totalBytes,
            FileCount = fileCount,
            SkippedCount = skippedCount,
            Elapsed = stopwatch.Elapsed
        };
    }

    private static string GetCategory(string extension)
    {
        if (VideoExtensions.Contains(extension)) return "视频";
        if (ArchiveExtensions.Contains(extension)) return "压缩包";
        if (InstallerExtensions.Contains(extension)) return "安装包";
        if (ImageExtensions.Contains(extension)) return "图片";
        if (DocumentExtensions.Contains(extension)) return "文档";
        return "其他";
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsExpectedFileSystemException(Exception exception) =>
        exception is UnauthorizedAccessException
            or IOException
            or System.Security.SecurityException
            or PathTooLongException
            or NotSupportedException;

    private static void AddError(List<string> errors, string path, Exception exception)
    {
        const int maximumRecordedErrors = 30;
        if (errors.Count < maximumRecordedErrors)
        {
            errors.Add($"{path}：{exception.Message}");
        }
    }

    private sealed record DirectoryWorkItem(string Path, string? BucketPath);

    private sealed class FolderAccumulator(string path)
    {
        public string Path { get; } = path;
        public long Size { get; set; }
        public long FileCount { get; set; }
    }
}
