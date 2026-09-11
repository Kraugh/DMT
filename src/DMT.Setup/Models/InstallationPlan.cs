namespace DMT.Setup.Models;

public sealed class InstallationPlan
{
    public int HttpsPort { get; init; }
    public string AccessMode { get; init; } = "local";
    public string? AccessAddress { get; init; }
    public string HttpsMode { get; init; } = "internal";
    public string AdminUsername { get; init; } = string.Empty;
    public string? AdminDisplayName { get; init; }

    // The password is transferred only through the private named pipe to the elevated helper.
    // It is deliberately excluded from manifests and logs.
    public string AdminPassword { get; init; } = string.Empty;
}
