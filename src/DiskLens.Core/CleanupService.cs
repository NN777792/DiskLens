namespace DiskLens.Core;

public sealed class CleanupService(CleanupPolicy policy, IFileRecycler recycler, IOperationJournal journal)
{
    // Caller runs this on the shell's STA thread, never Task.Run (COM shell requirement).
    public CleanupReport Execute(IReadOnlyList<CleanupItem> items, CancellationToken token = default, Action<int, int>? progress = null)
    {
        var started = DateTime.UtcNow;
        var outcomes = new List<CleanupOutcome>();
        var unique = items.DistinctBy(x => PathSafety.Normalize(x.File.FullPath), PathSafety.Comparer).ToArray();
        // A durable intent record is required before the first mutation.
        journal.Begin(unique);
        string? journalWarning = null;
        foreach (var item in unique)
        {
            if (token.IsCancellationRequested) break;
            CleanupOutcome outcome;
            var decision = policy.Revalidate(item.File);
            if (!decision.Allowed) outcome = new(item.File.FullPath, item.File.Length, false, decision.Reason);
            else
            {
                try
                {
                    recycler.Recycle(item.File.FullPath);
                    outcome = new(item.File.FullPath, item.File.Length, true, "已移入回收站，可在 Windows 回收站还原");
                }
                catch (Exception ex) { outcome = new(item.File.FullPath, item.File.Length, false, "未完成：" + ex.Message); }
            }
            outcomes.Add(outcome);
            try { journal.Append(outcome); }
            catch (Exception ex) { journalWarning = "记录写入失败，已停止后续操作：" + ex.Message; break; }
            progress?.Invoke(outcomes.Count, unique.Length);
        }
        return new CleanupReport(started, outcomes, token.IsCancellationRequested, journalWarning);
    }
}
