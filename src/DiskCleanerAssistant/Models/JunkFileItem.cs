namespace DiskCleanerAssistant.Models;

public sealed record JunkFileItem(string FullPath, long Size, DateTime LastModified);
