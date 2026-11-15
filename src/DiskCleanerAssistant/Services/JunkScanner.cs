using System.Diagnostics;
using DiskCleanerAssistant.Models;

namespace DiskCleanerAssistant.Services;

public sealed class JunkScanner : IJunkScanner
{
    private static readonly System.IO.EnumerationOptions ScanEnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public Task<JunkScanResult> ScanAsync(
        IProgress<JunkScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => Scan(progress, cancellationToken), cancellationToken);
    }

    private static JunkScanResult Scan(
        IProgress<JunkScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var definitions = CreateDefinitions();
        var categories = new List<JunkCategoryItem>();
        long totalFileCount = 0;
        long totalBytes = 0;
        var skippedCount = 0;

        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = ScanCategory(
                definition,
                progress,
                ref totalFileCount,
                ref totalBytes,
                ref skippedCount,
                stopwatch,
                cancellationToken);

            var category = new JunkCategoryItem
            {
                Id = definition.Id,
                Name = definition.Name,
                Description = definition.Description,
                IsRecommended = definition.IsRecommended
            };
            category.SetFiles(files);
            categories.Add(category);
        }

        stopwatch.Stop();
        return new JunkScanResult
        {
            Categories = categories,
            FileCount = totalFileCount,
            TotalBytes = totalBytes,
            SkippedCount = skippedCount,
            Elapsed = stopwatch.Elapsed
        };
    }

    private static List<JunkFileItem> ScanCategory(
        JunkCategoryDefinition definition,
        IProgress<JunkScanProgress>? progress,
        ref long totalFileCount,
        ref long totalBytes,
        ref int skippedCount,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var files = new List<JunkFileItem>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastProgressAt = TimeSpan.Zero;
        var cutoff = DateTime.Now - definition.MinimumAge;

        foreach (var root in definition.Roots
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root))
            {
                continue;
            }

            var directories = new Stack<string>();
            directories.Push(root);
            while (directories.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var currentDirectory = directories.Pop();

                IEnumerable<string> entries;
                try
                {
                    entries = Directory.EnumerateFileSystemEntries(
                        currentDirectory,
                        "*",
                        ScanEnumerationOptions);
                }
                catch (Exception ex) when (IsExpectedFileSystemException(ex))
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    foreach (var entry in entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            var attributes = File.GetAttributes(entry);
                            if ((attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                skippedCount++;
                                continue;
                            }

                            if ((attributes & FileAttributes.Directory) != 0)
                            {
                                directories.Push(entry);
                                continue;
                            }

                            if (!definition.FileFilter(entry) || !seenPaths.Add(entry))
                            {
                                continue;
                            }

                            var file = new FileInfo(entry);
                            if (definition.MinimumAge > TimeSpan.Zero && file.LastWriteTime > cutoff)
                            {
                                continue;
                            }

                            files.Add(new JunkFileItem(file.FullName, file.Length, file.LastWriteTime));
                            totalFileCount++;
                            totalBytes += file.Length;
                        }
                        catch (Exception ex) when (IsExpectedFileSystemException(ex))
                        {
                            skippedCount++;
                        }

                        if (stopwatch.Elapsed - lastProgressAt >= TimeSpan.FromMilliseconds(150))
                        {
                            progress?.Report(new JunkScanProgress(
                                definition.Name,
                                entry,
                                totalFileCount,
                                totalBytes,
                                skippedCount));
                            lastProgressAt = stopwatch.Elapsed;
                        }
                    }
                }
                catch (Exception ex) when (IsExpectedFileSystemException(ex))
                {
                    skippedCount++;
                }
            }
        }

        return files;
    }

    private static IReadOnlyList<JunkCategoryDefinition> CreateDefinitions()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        return
        [
            new JunkCategoryDefinition(
                "user-temp",
                "用户临时文件",
                "应用程序在当前用户临时目录中留下、且超过 24 小时未修改的文件。",
                true,
                TimeSpan.FromDays(1),
                [Path.GetTempPath()],
                _ => true),
            new JunkCategoryDefinition(
                "windows-temp",
                "系统临时文件",
                "Windows 临时目录中超过 24 小时未修改的文件；无权限和正在使用的项目会跳过。",
                true,
                TimeSpan.FromDays(1),
                [Path.Combine(windowsDirectory, "Temp")],
                _ => true),
            new JunkCategoryDefinition(
                "thumbnail-cache",
                "缩略图与图标缓存",
                "资源管理器生成的缩略图和图标缓存，Windows 会在需要时重新创建。",
                true,
                TimeSpan.Zero,
                [Path.Combine(localAppData, "Microsoft", "Windows", "Explorer")],
                IsExplorerCacheFile),
            new JunkCategoryDefinition(
                "crash-reports",
                "崩溃报告与转储",
                "应用崩溃转储和 Windows 错误报告，仅用于故障诊断。",
                true,
                TimeSpan.Zero,
                [
                    Path.Combine(localAppData, "CrashDumps"),
                    Path.Combine(localAppData, "Microsoft", "Windows", "WER"),
                    Path.Combine(commonAppData, "Microsoft", "Windows", "WER", "ReportArchive"),
                    Path.Combine(commonAppData, "Microsoft", "Windows", "WER", "ReportQueue")
                ],
                _ => true),
            new JunkCategoryDefinition(
                "graphics-cache",
                "DirectX 图形缓存",
                "显卡着色器缓存，删除后游戏或图形应用首次启动可能需要重新生成。",
                true,
                TimeSpan.FromDays(1),
                [Path.Combine(localAppData, "D3DSCache")],
                _ => true),
            new JunkCategoryDefinition(
                "browser-cache",
                "浏览器缓存",
                "Chrome、Edge、Firefox 和 Opera 的网页缓存；不会清理收藏夹、密码或浏览历史。",
                false,
                TimeSpan.FromDays(1),
                GetBrowserCacheDirectories(localAppData),
                _ => true)
        ];
    }

    private static IReadOnlyList<string> GetBrowserCacheDirectories(string localAppData)
    {
        var roots = new List<string>();
        AddChromiumProfileCaches(
            roots,
            Path.Combine(localAppData, "Google", "Chrome", "User Data"));
        AddChromiumProfileCaches(
            roots,
            Path.Combine(localAppData, "Microsoft", "Edge", "User Data"));

        AddFirefoxCaches(
            roots,
            Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles"));

        var operaRoot = Path.Combine(localAppData, "Opera Software");
        if (Directory.Exists(operaRoot))
        {
            foreach (var profile in EnumerateDirectoriesSafe(operaRoot))
            {
                roots.Add(Path.Combine(profile, "Cache"));
                roots.Add(Path.Combine(profile, "Code Cache"));
                roots.Add(Path.Combine(profile, "GPUCache"));
            }
        }

        return roots;
    }

    private static void AddChromiumProfileCaches(List<string> roots, string userDataRoot)
    {
        if (!Directory.Exists(userDataRoot))
        {
            return;
        }

        foreach (var profile in EnumerateDirectoriesSafe(userDataRoot))
        {
            var name = Path.GetFileName(profile);
            if (!name.Equals("Default", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            roots.Add(Path.Combine(profile, "Cache"));
            roots.Add(Path.Combine(profile, "Code Cache"));
            roots.Add(Path.Combine(profile, "GPUCache"));
        }
    }

    private static void AddFirefoxCaches(List<string> roots, string profilesRoot)
    {
        if (!Directory.Exists(profilesRoot))
        {
            return;
        }

        foreach (var profile in EnumerateDirectoriesSafe(profilesRoot))
        {
            roots.Add(Path.Combine(profile, "cache2"));
            roots.Add(Path.Combine(profile, "startupCache"));
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            return [];
        }
    }

    private static bool IsExplorerCacheFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".db", StringComparison.OrdinalIgnoreCase) &&
               (name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExpectedFileSystemException(Exception exception) =>
        exception is UnauthorizedAccessException
            or IOException
            or System.Security.SecurityException
            or PathTooLongException
            or NotSupportedException;

    private sealed record JunkCategoryDefinition(
        string Id,
        string Name,
        string Description,
        bool IsRecommended,
        TimeSpan MinimumAge,
        IReadOnlyList<string> Roots,
        Func<string, bool> FileFilter);
}
