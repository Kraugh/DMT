namespace DMT.Setup.Models;

public sealed class InstallationManifest
{
    public string Product { get; init; } = "DMT";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public List<ManagedResourceRecord> Resources { get; init; } = [];
}

public sealed class ManagedResourceRecord
{
    public string Type { get; init; } = string.Empty;
    public string Identifier { get; init; } = string.Empty;
    public bool CreatedByDmt { get; init; }
    public bool ManagedByDmt { get; init; }
    public ManagedResourceRemovalPolicy RemovalPolicy { get; init; } = ManagedResourceRemovalPolicy.Remove;
}

public enum ManagedResourceRemovalPolicy
{
    Remove,
    AskDefaultRemove,
    Preserve
}
