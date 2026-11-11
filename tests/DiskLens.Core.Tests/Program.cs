using DiskLens.Core;

// No external test framework: executable regression suite works on Windows and Linux.
var passed = 0;
var failed = 0;
var testRoot = Path.Combine(Path.GetTempPath(), "DiskLens-Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
async Task Test(string name, Func<string, Task> test)
{
    var folder = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try { await test(folder); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex); failed++; }
}
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
static FileSnapshot Snapshot(string path) { var f = new FileInfo(path); return new(f.FullName, f.Length, f.LastWriteTimeUtc, f.Attributes); }
static async Task<FileSnapshot> Make(string root, string name, int size, int daysOld = 0)
{
    var path = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllBytesAsync(path, new byte[size]);
    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-daysOld)); return Snapshot(path);
}
static CleanupPolicy Policy(string root) => new([Path.Combine(root, "Protected")], [Path.Combine(root, "Temp")]);

await Test("Scan aggregates nested directory bytes and types", async root =>
{
    await Make(root, "a.pdf", 17); await Make(root, "one/b.mp4", 31); await Make(root, "one/two/c.png", 23);
    var result = await new DiskScanner(Policy(root)).ScanAsync(root);
    Equal(71L, result.Root.TotalBytes); Equal(3L, result.Root.FileCount);
    Equal(54L, result.Root.Children.Single().TotalBytes); Equal(31L, result.LargestFiles.First().Length);
    Equal(3, result.Categories.Count); Equal(0L, result.Skipped);
});
await Test("Scan empty directory", async root => { var result = await new DiskScanner(Policy(root)).ScanAsync(root); Equal(0L, result.Root.TotalBytes); Equal(0, result.LargestFiles.Count); });
await Test("Cancellation returns explicitly partial result", async root =>
{
    await Make(root, "a.txt", 40); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    var result = await new DiskScanner(Policy(root)).ScanAsync(root, token: cancellation.Token);
    Check(result.Cancelled && result.IsPartial, "Cancellation must not look complete"); Equal(0L, result.Root.FileCount);
});
await Test("Missing scan root fails", async root =>
{
    try { await new DiskScanner(Policy(root)).ScanAsync(Path.Combine(root, "missing")); throw new Exception("Expected missing directory"); }
    catch (DirectoryNotFoundException) { }
});
await Test("Top file list is bounded and retains largest", async root =>
{
    for (var i = 1; i <= 530; i++) await Make(root, $"{i}.bin", i);
    var result = await new DiskScanner(Policy(root)).ScanAsync(root);
    Equal(DiskScanner.LargestFileLimit, result.LargestFiles.Count); Equal(530L, result.LargestFiles[0].Length); Equal(31L, result.LargestFiles[^1].Length);
    Equal(530L * 531 / 2, result.Root.TotalBytes);
});
await Test("Scan and revalidation skip symlinks and parent links", async root =>
{
    var outside = Path.Combine(root, "outside"); var inside = Path.Combine(root, "inside"); Directory.CreateDirectory(inside);
    var original = await Make(outside, "secret.bin", 100);
    Directory.CreateSymbolicLink(Path.Combine(inside, "link"), outside);
    File.CreateSymbolicLink(Path.Combine(inside, "file-link"), original.FullPath);
    var result = await new DiskScanner(Policy(root)).ScanAsync(inside);
    Equal(0L, result.Root.TotalBytes); Equal(2L, result.Skipped);
    var fake = original with { FullPath = Path.Combine(inside, "link", "secret.bin") };
    Check(!Policy(root).Revalidate(fake).Allowed, "Parent links must be rejected");
});
await Test("Linked scan root is rejected", async root =>
{
    Directory.CreateDirectory(Path.Combine(root, "real")); Directory.CreateSymbolicLink(Path.Combine(root, "link"), Path.Combine(root, "real"));
    try { await new DiskScanner(Policy(root)).ScanAsync(Path.Combine(root, "link")); throw new Exception("Expected rejection"); }
    catch (IOException) { }
});
await Test("Protected path boundary does not block similarly named folder", async root =>
{
    var protectedFile = await Make(root, "Protected/app.bin", 1);
    var personalFile = await Make(root, "Protected-copy/app.bin", 1);
    Check(!Policy(root).Evaluate(protectedFile).Allowed, "Protected file allowed");
    Check(Policy(root).Evaluate(personalFile).Allowed, "Sibling incorrectly blocked");
});
await Test("Old temp files suggested; recent and future temp files blocked", async root =>
{
    var old = await Make(root, "Temp/old.tmp", 1, 8); var recent = await Make(root, "Temp/recent.tmp", 1, 1); var future = await Make(root, "Temp/future.tmp", 1, -2);
    Check(Policy(root).Evaluate(old).Suggested, "Old temp should be suggested");
    Check(!Policy(root).Evaluate(recent).Allowed && !Policy(root).Evaluate(future).Allowed, "Recent or future file allowed");
});
await Test("AppData temp exception allows only that temp root", async root =>
{
    var policy = new CleanupPolicy([Path.Combine(root, "AppData")], [Path.Combine(root, "AppData", "Temp")]);
    var old = await Make(root, "AppData/Temp/a.tmp", 1, 8); var data = await Make(root, "AppData/Browser/a.db", 1, 20);
    Check(policy.Evaluate(old).Suggested, "Temp exception blocked"); Check(!policy.Evaluate(data).Allowed, "Application data allowed");
});
await Test("Size and age alone do not make a personal file suggested", async root =>
{
    var file = await Make(root, "Documents/important.bin", 1024, 365);
    var decision = Policy(root).Evaluate(file); Check(decision.Allowed && !decision.Suggested, "Personal file suggested as garbage");
});
await Test("System, hidden, readonly, offline and link attributes are protected", async root =>
{
    var file = await Make(root, "Temp/a.bin", 1, 8);
    foreach (var attr in new[] { FileAttributes.System, FileAttributes.Hidden, FileAttributes.ReadOnly, FileAttributes.Offline, FileAttributes.ReparsePoint })
        Check(!Policy(root).Evaluate(file with { Attributes = attr }).Allowed, $"Allowed {attr}");
});
await Test("Changed file is rejected before recycler call", async root =>
{
    var file = await Make(root, "a.txt", 10); await File.AppendAllTextAsync(file.FullPath, "changed");
    var recycler = new FakeRecycler(); var journal = new FakeJournal();
    var report = new CleanupService(Policy(root), recycler, journal).Execute([new(file, "test")]);
    Equal(0, recycler.Paths.Count); Equal(0, report.SuccessCount); Equal(1, journal.Results.Count);
});
await Test("Changed modification time rejected even if size unchanged", async root =>
{
    var file = await Make(root, "a.txt", 10); File.SetLastWriteTimeUtc(file.FullPath, file.LastWriteUtc.AddHours(1));
    Check(!Policy(root).Revalidate(file).Allowed, "Timestamp mutation not detected");
});
await Test("Missing file is rejected", async root =>
{
    var file = await Make(root, "a.txt", 10); File.Delete(file.FullPath); Check(!Policy(root).Revalidate(file).Allowed, "Missing file allowed");
});
await Test("Failed intent journal prevents every file operation", async root =>
{
    var file = await Make(root, "a.txt", 10); var recycler = new FakeRecycler();
    try { new CleanupService(Policy(root), recycler, new FakeJournal { FailBegin = true }).Execute([new(file, "test")]); throw new Exception("Expected journal error"); }
    catch (IOException) { } Equal(0, recycler.Paths.Count);
});
await Test("Cleanup deduplicates paths and records outcomes", async root =>
{
    var file = await Make(root, "a.txt", 10); var recycler = new FakeRecycler(); var journal = new FakeJournal();
    var report = new CleanupService(Policy(root), recycler, journal).Execute([new(file, "test"), new(file, "duplicate")]);
    Equal(1, recycler.Paths.Count); Equal(1, report.SuccessCount); Equal(10L, report.RecycledBytes); Equal(1, journal.Results.Count);
});
await Test("Recycle failure never falls back to permanent delete", async root =>
{
    var file = await Make(root, "a.txt", 10); var recycler = new FakeRecycler { Fail = true };
    var report = new CleanupService(Policy(root), recycler, new FakeJournal()).Execute([new(file, "test")]);
    Equal(0, report.SuccessCount); Check(File.Exists(file.FullPath), "File permanently deleted");
});
await Test("Failed outcome journal stops all later operations", async root =>
{
    var a = await Make(root, "a.txt", 10); var b = await Make(root, "b.txt", 20); var recycler = new FakeRecycler();
    var report = new CleanupService(Policy(root), recycler, new FakeJournal { FailAppend = true }).Execute([new(a, "test"), new(b, "test")]);
    Equal(1, recycler.Paths.Count); Check(report.JournalWarning != null, "Missing journal warning"); Equal(1, report.SuccessCount);
});
await Test("Cancelled cleanup performs no recycle calls", async root =>
{
    var file = await Make(root, "a.txt", 10); var recycler = new FakeRecycler(); using var token = new CancellationTokenSource(); token.Cancel();
    var report = new CleanupService(Policy(root), recycler, new FakeJournal()).Execute([new(file, "test")], token.Token);
    Equal(0, recycler.Paths.Count); Check(report.Cancelled, "Missing cancellation state");
});
await Test("Temp suggestions match scanner policy", async root =>
{
    await Make(root, "Temp/old.tmp", 20, 8); await Make(root, "Temp/new.tmp", 30, 1); await Make(root, "personal.bin", 40, 365);
    var scan = await new DiskScanner(Policy(root)).ScanAsync(root);
    Equal(1, scan.TempCandidates.Count); Equal(20L, scan.TempCandidates[0].Length);
});
await Test("Size formatter handles bytes and large values", root =>
{
    Equal("0 B", SizeFormatter.Format(0)); Equal("1 KB", SizeFormatter.Format(1024)); Equal("1 GB", SizeFormatter.Format(1024L * 1024 * 1024)); return Task.CompletedTask;
});

Directory.Delete(testRoot, recursive: true);
Console.WriteLine($"\n{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;

sealed class FakeRecycler : IFileRecycler
{
    public List<string> Paths { get; } = [];
    public bool Fail { get; init; }
    public void Recycle(string fullPath) { if (Fail) throw new IOException("Simulated shell failure"); Paths.Add(fullPath); }
}
sealed class FakeJournal : IOperationJournal
{
    public List<CleanupOutcome> Results { get; } = [];
    public bool FailBegin { get; init; }
    public bool FailAppend { get; init; }
    public void Begin(IReadOnlyList<CleanupItem> items) { if (FailBegin) throw new IOException("Intent write failed"); }
    public void Append(CleanupOutcome outcome) { if (FailAppend) throw new IOException("Outcome write failed"); Results.Add(outcome); }
}
