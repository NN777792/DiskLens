namespace DiskCleanerAssistant.Models;

public sealed record SizeOption(string Label, long Bytes)
{
    public override string ToString() => Label;
}
