using System.Diagnostics;

namespace DiskLens.Core;

public sealed class DiskScanner(CleanupPolicy policy)
{
    public const int LargestFileLimit = 500;
    public const int TempCandidateLimit = 2000;
    public const int DirectoryLimit = 250_000;
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0,
        ReturnSpecialDirectories = false
    };

    public Task<ScanResult> ScanAsync(string path, IProgress<ScanProgress>? progress = null, CancellationToken token = default)
        => Task.Run(() => Scan(path, progress, token), CancellationToken.None);

    private ScanResult Scan(string path, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var fullPath = PathSafety.Normalize(path);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException("扫描目录不存在或无法访问。");
        if (PathSafety.HasUnsafeAncestor(fullPath)) throw new IOException("请选择实际的本地目录，暂不扫描链接或云端占位目录。");
        var root = new DirectoryNode(fullPath);
        var stack = new Stack<DirectoryNode>();
        var all = new List<DirectoryNode> { root };
        var largest = new PriorityQueue<FileSnapshot, long>();
        var temp = new List<FileSnapshot>();
        var issues = new List<ScanIssue>();
        var categories = new Dictionary<string, (long bytes, long count)>();
        long fileCount = 0, bytes = 0, skipped = 0;
        bool limitReached = false;
        var started = DateTime.UtcNow;
        var watch = Stopwatch.StartNew();
        long lastProgress = -150;
        stack.Push(root);
        void Issue(string issuePath, string reason)
        {
            skipped++;
            if (issues.Count < 500) issues.Add(new ScanIssue(issuePath, reason));
        }
        void Report(string current, bool force = false)
        {
            if (force || watch.ElapsedMilliseconds - lastProgress >= 150)
            {
                progress?.Report(new ScanProgress(fileCount, bytes, all.Count, skipped, current));
                lastProgress = watch.ElapsedMilliseconds;
            }
        }
        while (stack.Count > 0 && !token.IsCancellationRequested)
        {
            var node = stack.Pop();
            try
            {
                // Recheck before traversing: the directory may have changed since discovery.
                if (PathSafety.HasUnsafeAncestor(node.FullPath)) { Issue(node.FullPath, "跳过链接或云端占位目录"); continue; }
                foreach (var entry in new DirectoryInfo(node.FullPath).EnumerateFileSystemInfos("*", Options))
                {
                    if (token.IsCancellationRequested) break;
                    try
                    {
                        var attributes = entry.Attributes;
                        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
                        { Issue(entry.FullName, "跳过链接或云端占位文件"); continue; }
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (all.Count >= DirectoryLimit) { limitReached = true; Issue(entry.FullName, "目录数达到本次扫描上限"); continue; }
                            var child = new DirectoryNode(entry.FullName, node);
                            node.Children.Add(child);
                            all.Add(child);
                            stack.Push(child);
                        }
                        else if (entry is FileInfo file)
                        {
                            var snapshot = new FileSnapshot(file.FullName, file.Length, file.LastWriteTimeUtc, attributes);
                            node.DirectBytes += snapshot.Length;
                            node.FileCount++;
                            bytes += snapshot.Length;
                            fileCount++;
                            var category = Categorize(snapshot.Extension);
                            categories.TryGetValue(category, out var aggregate);
                            categories[category] = (aggregate.bytes + snapshot.Length, aggregate.count + 1);
                            if (largest.Count < LargestFileLimit) largest.Enqueue(snapshot, snapshot.Length);
                            else if (largest.TryPeek(out _, out var smallest) && snapshot.Length > smallest)
                            { largest.Dequeue(); largest.Enqueue(snapshot, snapshot.Length); }
                            if (temp.Count < TempCandidateLimit && policy.Evaluate(snapshot).Suggested) temp.Add(snapshot);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    { Issue(entry.FullName, ex.Message); }
                    Report(node.FullPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { Issue(node.FullPath, ex.Message); }
            Report(node.FullPath);
        }
        // Children are always discovered after parents. Reverse aggregation is stack-safe.
        for (var i = all.Count - 1; i >= 0; i--)
        {
            var node = all[i];
            node.TotalBytes += node.DirectBytes;
            if (node.Parent != null) { node.Parent.TotalBytes += node.TotalBytes; node.Parent.FileCount += node.FileCount; }
        }
        Report(fullPath, true);
        return new ScanResult(root, largest.UnorderedItems.Select(x => x.Element).OrderByDescending(x => x.Length).ToArray(),
            temp.OrderByDescending(x => x.Length).ToArray(), categories.Select(x => new FileCategory(x.Key, x.Value.bytes, x.Value.count)).OrderByDescending(x => x.Bytes).ToArray(),
            issues, skipped, token.IsCancellationRequested, limitReached, started, watch.Elapsed);
    }

    private static string Categorize(string ext) => ext switch
    {
        ".mp4" or ".mkv" or ".mov" or ".avi" or ".wmv" or ".webm" => "视频",
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".heic" or ".raw" or ".psd" or ".svg" => "图片与设计",
        ".doc" or ".docx" or ".pdf" or ".txt" or ".xlsx" or ".pptx" or ".md" or ".csv" => "文档",
        ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".iso" => "压缩包与镜像",
        ".exe" or ".msi" or ".msix" or ".dll" => "程序与安装包",
        ".mp3" or ".wav" or ".flac" or ".m4a" or ".aac" => "音频",
        _ => "其他文件"
    };
}
