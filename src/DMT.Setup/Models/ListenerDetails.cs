namespace DMT.Setup.Models;

public sealed class ListenerDetails
{
    public ListeningEndpoint Endpoint { get; init; } = new();
    public string ExecutablePath { get; init; } = "";
    public string FileDescription { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string CompanyName { get; init; } = "";
    public string Publisher { get; init; } = "";
    public bool? IsSigned { get; init; }
    public string KnowledgeTitleKey { get; init; } = "setup.network.knowledge.unknown.title";
    public string KnowledgeBodyKey { get; init; } = "setup.network.knowledge.unknown.body";
}
