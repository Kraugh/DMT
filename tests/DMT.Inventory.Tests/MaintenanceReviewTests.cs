using System.Text.Json;
using DMT.Inventory;

internal static class MaintenanceReviewTests
{
    public static void Run(Action<bool, string> check)
    {
        MaintenanceReview Parse(object sections) => MaintenanceReviewReader.Read("asset", "snapshot", "TEST-PC", "2026-10-19T08:00:00+00:00", JsonSerializer.Serialize(sections));
        var ok = Parse(new { maintenance = new { status = "ok", data = new { windowsUpdate = new { status = "ok" } } } });
        check(!ok.NeedsReview && ok.Findings.Count == 0, "maintenance_ok_not_in_review");
        var bios = Parse(new { maintenance = new { status = "ok", data = new { oem = new { status = "action_required", biosUpdate = new { status = "urgent_action_required", currentVersion = "1.0", targetVersion = "2.0", name = "BIOS", reason = "manual_review", automaticInstall = false } } } } });
        check(bios.NeedsReview && bios.Findings.Any(f => f.Kind == "bios" && f.UpdateCandidate && f.InstalledVersion == "1.0" && f.TargetVersion == "2.0"), "maintenance_bios_in_ok_report");
        check(!bios.RemoteUpdatesAvailable && bios.RemoteUpdateReason == "remote_channel_not_configured", "maintenance_no_fake_remote_executor");
        var gpu = Parse(new { maintenance = new { status = "ok", data = new { gpuDrivers = new { mode = "advisory_only", status = "action_required", rebootRequired = false, devices = new[] { new { name = "TEST GPU", status = "update_available", updateAvailable = true, installedVersion = "100", candidate = new { version = "101" } } } } } } });
        check(gpu.Findings.Single().Kind == "gpu" && gpu.Findings.Single().UpdateCandidate, "maintenance_gpu_candidate");
        check(gpu.Findings.Single().RebootRequired == false && !gpu.RemoteUpdatesAvailable, "maintenance_report_reboot_false_not_execution_guarantee");
        var error = Parse(new { maintenance = new { status = "error", errors = new[] { new { source = "oem", code = "<img src=x>" } }, data = new { oem = new { status = "verification_failed", rebootRequired = true, apply = new { outcome = "deferred_by_user_activity" }, updates = new[] { new { name = "Driver", status = "restart_required", rebootRequired = true } } } } } });
        check(error.Findings.Any(f => f.Kind == "reported_error" && f.Detail == "<img src=x>"), "maintenance_preserves_native_error_text");
        check(error.Findings.Any(f => f.Kind == "oem_update" && f.RebootRequired == true), "maintenance_pending_reboot_visible");
        check(error.Findings.Any(f => f.Kind == "oem_apply" && f.Status == "deferred_by_user_activity"), "maintenance_deferred_install_visible");
        var generic = Parse(new { maintenance = new { status = "partial" } });
        check(generic.NeedsReview && generic.Findings.Single().Kind == "maintenance", "maintenance_partial_fallback");
        var absent = Parse(new { });
        check(!absent.NeedsReview && absent.Status == null, "maintenance_missing_not_invented_failure");
        var current = Parse(new { maintenance = new { status = "ok", data = new { gpuDrivers = new { status = "up_to_date_in_selected_catalogue", devices = new[] { new { status = "up_to_date_in_selected_catalogue", updateAvailable = false } } } } } });
        check(!current.NeedsReview, "maintenance_current_gpu_excluded");
    }
}
