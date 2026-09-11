namespace DMT.Setup.Models;

public sealed class InstallationProgress
{
    public string StatusKey { get; init; } = string.Empty;
    public int Percent { get; init; }
    public bool IsCompleted { get; init; }
    public bool IsError { get; init; }
}
