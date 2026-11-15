using Microsoft.VisualBasic.FileIO;

namespace DiskCleanerAssistant.Services;

public sealed class RecycleBinService : IRecycleBinService
{
    public Task<DeleteResult> MoveToRecycleBinAsync(
        IReadOnlyCollection<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var deleted = new List<string>();
            var failures = new List<DeleteFailure>();

            foreach (var path in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!File.Exists(path))
                    {
                        failures.Add(new DeleteFailure(path, "文件已经不存在"));
                        continue;
                    }

                    FileSystem.DeleteFile(
                        path,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin,
                        UICancelOption.ThrowException);
                    deleted.Add(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
                {
                    failures.Add(new DeleteFailure(path, ex.Message));
                }
            }

            return new DeleteResult(deleted, failures);
        }, cancellationToken);
    }
}
