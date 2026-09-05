namespace DMT.Setup.Models;

public sealed class ListeningEndpoint
{
    public string Protocol { get; init; } = "TCP";
    public string Address { get; init; } = "";
    public int Port { get; init; }
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public string ProcessPath { get; init; } = "";
    public string Services { get; init; } = "";
    public string Exposure { get; init; } = "";
    public bool IsLoopback { get; init; }
    public bool IsAny { get; init; }
}
