using System.IO;
using System.Text.Json;
using DiskLens.Core;

namespace DiskLens.App.Services;

public sealed class JsonJournal : IOperationJournal
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskLens", "History");
    private string? currentPath;
    public void Begin(IReadOnlyList<CleanupItem> items)
    {
        Directory.CreateDirectory(Folder);
        currentPath = Path.Combine(Folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + ".jsonl");
        Write(new { kind = "intent", utc = DateTime.UtcNow, files = items.Select(x => new { path = x.File.FullPath, size = x.File.Length, modifiedUtc = x.File.LastWriteUtc, reason = x.Reason }) });
    }
    public void Append(CleanupOutcome outcome) => Write(new { kind = "outcome", utc = DateTime.UtcNow, result = outcome });
    private void Write(object value)
    {
        if (currentPath == null) throw new InvalidOperationException("尚未创建操作记录。");
        using var stream = new FileStream(currentPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value) + "\n");
        stream.Write(bytes); stream.Flush(flushToDisk: true);
    }
    public static IReadOnlyList<HistoryEntry> Read()
    {
        if (!Directory.Exists(Folder)) return [];
        var history = new List<HistoryEntry>();
        foreach (var path in Directory.EnumerateFiles(Folder, "*.jsonl").OrderByDescending(x => x).Take(200))
        {
            try
            {
                DateTime date = File.GetCreationTime(path); int planned = 0, ok = 0, failed = 0; long bytes = 0;
                foreach (var line in File.ReadLines(path))
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.GetProperty("kind").GetString() == "intent") { planned = root.GetProperty("files").GetArrayLength(); date = root.GetProperty("utc").GetDateTime().ToLocalTime(); }
                    else if (root.TryGetProperty("result", out var result))
                    { if (result.GetProperty("Success").GetBoolean()) { ok++; bytes += result.GetProperty("Bytes").GetInt64(); } else failed++; }
                }
                history.Add(new(date.ToString("yyyy-MM-dd HH:mm:ss"), ok, failed, Math.Max(0, planned - ok - failed), SizeFormatter.Format(bytes), path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
            { history.Add(new(Path.GetFileName(path), 0, 0, 0, "记录不完整，请查看原始文件", path)); }
        }
        return history;
    }
}
public sealed record HistoryEntry(string Date, int Success, int Failed, int Unfinished, string Size, string Path);
