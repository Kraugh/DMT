using System.Text.Json;

namespace DMT.Inventory;

public sealed record MaintenanceFinding(string Kind, string Status, string? Name, string? Detail,
    string? InstalledVersion = null, string? TargetVersion = null, bool UpdateCandidate = false, bool? RebootRequired = null);
public sealed record MaintenanceReview(string AssetId, string SnapshotId, string Hostname, string CollectedAt,
    string? Status, bool NeedsReview, bool RemoteUpdatesAvailable, string RemoteUpdateReason, IReadOnlyList<MaintenanceFinding> Findings);

public static class MaintenanceReviewReader
{
    private static readonly HashSet<string> Attention = new(StringComparer.OrdinalIgnoreCase)
    { "error", "partial", "failed", "action_required", "urgent_action_required", "verification_failed", "restart_required", "deferred_by_user_activity", "incomplete" };
    public static MaintenanceReview Read(string asset, string snapshot, string hostname, string collectedAt, string sectionsJson)
    {
        using var document = JsonDocument.Parse(sectionsJson);
        var maintenance = Child(document.RootElement, "maintenance"); var data = Child(maintenance, "data");
        var findings = new List<MaintenanceFinding>();
        foreach (var error in Items(Child(maintenance, "errors")))
            findings.Add(new("reported_error", "error", Value(error, "source"), Value(error, "code")));
        var windows = Child(data, "windowsUpdate"); var oem = Child(data, "oem"); var gpu = Child(data, "gpuDrivers");
        void AddStatus(JsonElement section, string kind)
        {
            var status = Value(section, "status");
            if (status != null && Attention.Contains(status)) findings.Add(new(kind, status, null, Value(section, "errorCode"), RebootRequired: Flag(section, "rebootRequired")));
            else if (Flag(section, "rebootRequired") == true) findings.Add(new(kind, "restart_required", null, null, RebootRequired: true));
        }
        AddStatus(windows, "windows_update"); AddStatus(oem, "oem");
        var bios = Child(oem, "biosUpdate");
        if (bios.ValueKind == JsonValueKind.Object && (Attention.Contains(Value(bios, "status") ?? "") ||
            Value(bios, "targetVersion") is { } target && target != Value(bios, "currentVersion")))
            findings.Add(new("bios", Value(bios, "status") ?? "action_required", Value(bios, "name"), Value(bios, "reason"),
                Value(bios, "currentVersion"), Value(bios, "targetVersion"), true, Flag(bios, "rebootRequired")));
        foreach (var update in Items(Child(oem, "updates")))
        {
            var status = Value(update, "status");
            if (Attention.Contains(status ?? "") || Flag(update, "rebootRequired") == true)
                findings.Add(new("oem_update", status ?? "restart_required", Value(update, "name"), Value(update, "id"),
                    Value(update, "currentVersion"), Value(update, "targetVersion"), false, Flag(update, "rebootRequired")));
        }
        var gpuFindings = 0;
        foreach (var device in Items(Child(gpu, "devices")))
        {
            var status = Value(device, "status"); var available = Flag(device, "updateAvailable") == true;
            if (!available && !Attention.Contains(status ?? "")) continue;
            gpuFindings++;
            findings.Add(new("gpu", status ?? "action_required", Value(device, "name"), Value(device, "reason"),
                Value(device, "installedVersion"), Value(Child(device, "candidate"), "version"), available, Flag(gpu, "rebootRequired")));
        }
        if (gpuFindings == 0) AddStatus(gpu, "gpu");
        var apply = Child(oem, "apply"); var outcome = Value(apply, "outcome");
        if (Attention.Contains(outcome ?? "")) findings.Add(new("oem_apply", outcome!, null, Value(apply, "errorCode")));
        AddStatus(Child(data, "wingetExceptions"), "software_update");
        foreach (var package in Items(Child(Child(data, "wingetExceptions"), "packages")))
            if (Attention.Contains(Value(package, "status") ?? ""))
                findings.Add(new("software_update", Value(package, "status")!, Value(package, "id") ?? Value(package, "name"), Value(package, "errorCode")));
        var overall = Value(maintenance, "status");
        if (findings.Count == 0 && Attention.Contains(overall ?? "")) findings.Add(new("maintenance", overall!, null, null));
        return new(asset, snapshot, hostname, collectedAt, overall, findings.Count > 0, false, "remote_channel_not_configured", findings.Distinct().ToArray());
    }
    private static JsonElement Child(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var child) ? child : default;
    private static string? Value(JsonElement element, string key)
    {
        var value = Child(element, key);
        return value.ValueKind is JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
    }
    private static bool? Flag(JsonElement element, string key) => Child(element, key).ValueKind switch
    { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
    private static IEnumerable<JsonElement> Items(JsonElement element) => element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().ToArray() : [];
}
