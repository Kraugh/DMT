namespace DMT.Setup.Models;

public sealed class NetworkInterfaceInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string InterfaceType { get; init; } = "";
    public IReadOnlyList<string> IPv4Addresses { get; init; } = [];
    public IReadOnlyList<string> IPv6Addresses { get; init; } = [];

    public string DisplayLabel
    {
        get
        {
            var addresses = IPv4Addresses.Count > 0 ? IPv4Addresses : IPv6Addresses;
            return addresses.Count > 0 ? $"{Name} — {string.Join(", ", addresses)}" : Name;
        }
    }
}
