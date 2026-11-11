namespace DiskLens.Core;

public static class PathSafety
{
    public static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool IsWithin(string path, string root)
    {
        var full = Normalize(path);
        var parent = Normalize(root);
        return full.Equals(parent, Comparison) || full.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, Comparison);
    }

    public static bool HasUnsafeAncestor(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            var attributes = File.GetAttributes(current);
            // Offline files may trigger a cloud download; reparse points may escape the scan root.
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) return true;
            current = Path.GetDirectoryName(current);
        }
        return false;
    }
}

public sealed class CleanupPolicy
{
    private readonly string[] protectedRoots;
    private readonly string[] tempRoots;
    private readonly Func<DateTime> utcNow;
    public CleanupPolicy(IEnumerable<string> protectedRoots, IEnumerable<string> tempRoots, Func<DateTime>? utcNow = null)
    {
        this.protectedRoots = protectedRoots.Where(x => !string.IsNullOrWhiteSpace(x)).Select(PathSafety.Normalize).ToArray();
        this.tempRoots = tempRoots.Where(x => !string.IsNullOrWhiteSpace(x)).Select(PathSafety.Normalize).ToArray();
        this.utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public static CleanupPolicy ForCurrentUser()
    {
        static string Folder(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        return new CleanupPolicy([
            Folder(Environment.SpecialFolder.Windows), Folder(Environment.SpecialFolder.ProgramFiles),
            Folder(Environment.SpecialFolder.ProgramFilesX86), Folder(Environment.SpecialFolder.CommonApplicationData),
            Folder(Environment.SpecialFolder.LocalApplicationData), Folder(Environment.SpecialFolder.ApplicationData),
            Folder(Environment.SpecialFolder.CommonProgramFiles), Folder(Environment.SpecialFolder.CommonProgramFilesX86)
        ], [Path.Combine(Folder(Environment.SpecialFolder.LocalApplicationData), "Temp")]);
    }

    public CleanupDecision Evaluate(FileSnapshot file)
    {
        if ((file.Attributes & (FileAttributes.System | FileAttributes.Hidden | FileAttributes.ReparsePoint | FileAttributes.Offline | FileAttributes.ReadOnly | FileAttributes.Directory)) != 0)
            return new(false, false, "受保护、只读、链接或云端占位文件");
        var full = PathSafety.Normalize(file.FullPath);
        // Never offer paging files, boot metadata or an application's installed directory.
        var segments = full.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(x => x.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) || x.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)))
            return new(false, false, "Windows 管理的目录");
        var temp = tempRoots.FirstOrDefault(root => PathSafety.IsWithin(full, root));
        if (temp != null)
        {
            // An override is valid only for the AppData container that contains this specific temp root.
            if (protectedRoots.Any(root => PathSafety.IsWithin(full, root) && !PathSafety.IsWithin(temp, root)))
                return new(false, false, "位于系统或应用程序目录");
            if (utcNow() - file.LastWriteUtc < TimeSpan.FromDays(7))
                return new(false, false, "临时文件最近 7 天内仍有修改");
            return new(true, true, "当前用户临时目录内，至少 7 天未修改；应用可能重新生成");
        }
        if (protectedRoots.Any(root => PathSafety.IsWithin(full, root)))
            return new(false, false, "系统或应用数据受保护，请使用 Windows 或软件自身的清理功能");
        return new(true, false, "个人文件：请确认用途，并核实重要资料已有备份");
    }

    public CleanupDecision Revalidate(FileSnapshot original)
    {
        try
        {
            if (PathSafety.HasUnsafeAncestor(original.FullPath)) return new(false, false, "文件或上级目录是链接或云端占位目录");
            var file = new FileInfo(original.FullPath);
            file.Refresh();
            if (!file.Exists) return new(false, false, "文件已不存在");
            if (file.Length != original.Length || file.LastWriteTimeUtc != original.LastWriteUtc)
                return new(false, false, "文件已在扫描后发生变化，请重新扫描");
            return Evaluate(new FileSnapshot(file.FullName, file.Length, file.LastWriteTimeUtc, file.Attributes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(false, false, "无法重新检查文件：" + ex.Message);
        }
    }
}
