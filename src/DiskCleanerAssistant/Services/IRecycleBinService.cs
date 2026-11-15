namespace DiskCleanerAssistant.Services;

public interface IRecycleBinService
{
    Task<DeleteResult> MoveToRecycleBinAsync(
        IReadOnlyCollection<string> filePaths,
        CancellationToken cancellationToken = default);
}

public sealed record DeleteFailure(string Path, string Message);

public sealed record DeleteResult(
    IReadOnlyList<string> DeletedPaths,
    IReadOnlyList<DeleteFailure> Failures);
