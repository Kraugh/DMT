using System.Net;
using System.Text;
using System.Text.Json;
using DMT.Inventory;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration["Dmt:RuntimeConfig"] is { } runtimePath)
{
    var runtime = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(runtimePath)) ?? throw new InvalidDataException("runtime_configuration_missing");
    builder.Configuration.AddInMemoryCollection(runtime.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)));
}
builder.Services.AddWindowsService(options => options.ServiceName = "DMT");
var installed = builder.Configuration.GetValue("Dmt:Installed", false);
if (installed)
{
    DMT.Web.LocalAuthentication.Configure(builder);
    if (builder.Configuration.GetValue("Dmt:CaddyEnabled", true)) builder.Services.AddHostedService<DMT.Web.CaddyService>();
}
var database = builder.Configuration["Dmt:Database"] ?? Path.Combine("data", "dmt.sqlite");
var tenant = builder.Configuration["Dmt:Tenant"] ?? "local";
var site = builder.Configuration["Dmt:Site"] ?? "default";
if (builder.Configuration["Dmt:Acquire"] == "true")
{
    if (new[] { "ArchiveCompress", "ArchiveMove", "ArchivePlan", "Import" }.Any(mode => builder.Configuration["Dmt:" + mode] == "true"))
        throw new ArgumentException("acquisition_modes_are_exclusive");
    var source = builder.Configuration["Dmt:Source"] ?? throw new ArgumentException("source_required");
    var journals = builder.Configuration["Dmt:ArchiveJournals"] ?? throw new ArgumentException("local_journal_directory_required");
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    var result = AcquisitionWorkflow.Run(database, source, tenant, site, journals,
        (phase, count) => Console.Error.WriteLine(JsonSerializer.Serialize(new { phase, count })), cancellation.Token,
        reconcileConflicts: builder.Configuration.GetValue("Dmt:ReconcileConflicts", false),
        pruneSupersededConflicts: builder.Configuration.GetValue("Dmt:PruneSupersededConflicts", false));
    Console.WriteLine(JsonSerializer.Serialize(result));
    if (result.Import.Rejected > 0 || result.Issues.Count > 0 || result.Archive?.Blocked > 0 ||
        result.Compression?.Months.Any(m => m.Status != "archived") == true || result.Compression?.UnrecognizedFiles.Count > 0)
        Environment.ExitCode = 2;
    return;
}
if (builder.Configuration["Dmt:ArchiveCompress"] == "true")
{
    if (builder.Configuration["Dmt:ArchiveMove"] == "true" || builder.Configuration["Dmt:ArchivePlan"] == "true" || builder.Configuration["Dmt:Import"] == "true")
        throw new ArgumentException("archive_modes_are_exclusive");
    var source = builder.Configuration["Dmt:Source"] ?? throw new ArgumentException("source_required");
    var journal = builder.Configuration["Dmt:ArchiveJournal"] ?? throw new ArgumentException("local_archive_journal_required");
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    var compressionTimer = System.Diagnostics.Stopwatch.StartNew();
    using var elapsedProgress = new Timer(_ => Console.Error.WriteLine(JsonSerializer.Serialize(new { phase = "monthly_archive", elapsedMs = compressionTimer.ElapsedMilliseconds })), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    var result = ArchiveCompressor.CatchUp(database, source, tenant, journal, DateTimeOffset.UtcNow,
        (month, elapsed) => Console.Error.WriteLine(JsonSerializer.Serialize(new { month, elapsedMs = elapsed })), cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    if (result.Months.Any(m => m.Status != "archived") || result.UnrecognizedFiles.Count > 0) Environment.ExitCode = 2;
    return;
}
if (builder.Configuration["Dmt:ArchiveMove"] == "true")
{
    if (builder.Configuration["Dmt:ArchivePlan"] == "true" || builder.Configuration["Dmt:Import"] == "true") throw new ArgumentException("archive_modes_are_exclusive");
    var planFile = builder.Configuration["Dmt:ArchivePlanFile"] ?? throw new ArgumentException("approved_plan_required");
    var journal = builder.Configuration["Dmt:ArchiveJournal"] ?? throw new ArgumentException("local_archive_journal_required");
    var plan = JsonSerializer.Deserialize<ArchiveSimulation>(File.ReadAllText(planFile)) ?? throw new InvalidDataException("invalid_approved_plan");
    if (plan.Tenant != tenant) throw new InvalidDataException("approved_plan_tenant_mismatch");
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    var summary = ArchiveMover.Apply(database, plan, journal,
        (result, elapsed) => Console.Error.WriteLine(JsonSerializer.Serialize(new { result.Source, result.Status, result.Reason, elapsedMs = elapsed })), cancellation.Token,
        builder.Configuration.GetValue("Dmt:ArchiveConcurrency", 4));
    Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    if (summary.Blocked > 0) Environment.ExitCode = 2;
    return;
}
if (builder.Configuration["Dmt:ArchivePlan"] == "true")
{
    if (builder.Configuration["Dmt:Import"] == "true") throw new ArgumentException("archive_simulation_cannot_import");
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    var source = builder.Configuration["Dmt:Source"] ?? throw new ArgumentException("source_required");
    var plan = ArchivePlanner.Simulate(database, source, tenant, DateTimeOffset.UtcNow,
        (count, elapsed) => Console.Error.WriteLine(JsonSerializer.Serialize(new { inspected = count, elapsedMs = elapsed })), cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));
    if (plan.Issues.Count > 0 || plan.Files.Any(f => f.Action == "blocked") || plan.Months.Any(m => m.Action == "blocked")) Environment.ExitCode = 2;
    return;
}
var store = new InventoryStore(database);
if (builder.Configuration["Dmt:Import"] == "true")
{
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    var source = builder.Configuration["Dmt:Source"] ?? throw new ArgumentException("source_required");
    var summary = FolderImporter.Import(store, source, tenant, site,
        (count, elapsed) => Console.Error.WriteLine(JsonSerializer.Serialize(new { processed = count, elapsedMs = elapsed })), cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(summary));
    if (summary.Rejected > 0) Environment.ExitCode = 2;
    return;
}
// Both development preview and installed backend remain loopback-only.
var urls = builder.Configuration["urls"] ?? "http://127.0.0.1:5080";
if (builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
    throw new InvalidOperationException("endpoint_configuration_not_supported");
foreach (var url in urls.Split(';'))
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
        !(uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip)))
        throw new InvalidOperationException("loopback_required");
builder.WebHost.UseUrls(urls);
if (builder.Configuration["Dmt:ArchiveCatchUp"] == "true")
{
    var source = builder.Configuration["Dmt:Source"] ?? throw new ArgumentException("source_required");
    var journals = builder.Configuration["Dmt:ArchiveJournals"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DMT", "Development", "ArchivePlans");
    builder.Services.AddHostedService(provider => new DMT.Web.ArchiveCatchUpService(database, source, tenant, journals,
        provider.GetRequiredService<ILogger<DMT.Web.ArchiveCatchUpService>>()));
}
var app = builder.Build();
if (installed)
{
    var forwarded = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
    { ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto, ForwardLimit = 1 };
    forwarded.KnownProxies.Add(IPAddress.Loopback); forwarded.KnownProxies.Add(IPAddress.IPv6Loopback);
    app.UseForwardedHeaders(forwarded); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-DMT-Instance"] = builder.Configuration["Dmt:Instance"] ?? "installed";
        if ((context.Request.Path == "/" || context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/api/languages")) &&
            context.User.Identity?.IsAuthenticated != true)
        { await context.ChallengeAsync(); return; }
        await next(context);
    });
    DMT.Web.LocalAuthentication.Map(app, builder.Configuration["Dmt:AdminFile"] ?? throw new InvalidDataException("admin_file_required"));
}
var languages = new LanguageCatalog(Path.Combine(AppContext.BaseDirectory, "languages"));
app.MapGet("/", () => Results.File(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"), "text/html; charset=utf-8"));
app.MapGet("/app.css", () => Results.File(Path.Combine(AppContext.BaseDirectory, "wwwroot", "app.css"), "text/css; charset=utf-8"));
app.MapGet("/app.js", () => Results.File(Path.Combine(AppContext.BaseDirectory, "wwwroot", "app.js"), "text/javascript; charset=utf-8"));
app.MapGet("/report-text.js", () => Results.File(Path.Combine(AppContext.BaseDirectory, "wwwroot", "report-text.js"), "text/javascript; charset=utf-8"));
app.MapGet("/logo.png", () => Results.File(Path.Combine(AppContext.BaseDirectory, "assets", "dmt-logo.png"), "image/png"));
app.MapGet("/api/languages", () => languages.Visible);
app.MapGet("/api/languages/{code}", (string code) => languages.Get(code));
app.MapGet("/api/systems", (string? site, string? name, string? asset) => store.Systems(tenant, site, name, asset));
app.MapGet("/api/software", (string? site, string? name, string? publisher, string? version, string? asset) => store.Software(tenant, site, name, publisher, version, asset));
app.MapGet("/api/products", (string? site) => store.Products(tenant, site));
app.MapGet("/api/assets/{asset}/history", (string asset) => store.History(tenant, asset));
app.MapGet("/api/maintenance", () => store.Maintenance(tenant).Where(m => m.NeedsReview));
app.MapGet("/api/assets/{asset}/maintenance", (string asset) =>
    store.Maintenance(tenant, asset).FirstOrDefault() is { } review ? Results.Ok(review) : Results.NotFound());
app.MapGet("/api/import-attempts", () => store.Attempts(tenant));
app.MapGet("/api/software.csv", (string? site, string? name, string? publisher, string? version, string? asset) =>
{
    var rows = store.Software(tenant, site, name, publisher, version, asset);
    var columns = new[] { "asset_id", "hostname", "snapshot_id", "name", "publisher", "version", "source", "software_status" };
    var csv = new StringBuilder().AppendLine(string.Join(',', columns));
    foreach (var row in rows) csv.AppendLine(string.Join(',', columns.Select(key => SoftwareExports.CsvCell(row[key]))));
    return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", "DMT-software.csv");
});
app.MapGet("/api/software-computers.txt", (string? site, string? name, string? publisher, string? version, string? asset) =>
{
    var output = SoftwareExports.Computers(store.Software(tenant, site, name, publisher, version, asset));
    return Results.File(Encoding.UTF8.GetBytes(output), "text/plain; charset=utf-8", "DMT-computers.txt");
});
app.MapGet("/api/software-computers.csv", (string? site, string? name, string? publisher, string? version, string? asset) =>
{
    var output = SoftwareExports.ComputerVersions(store.Software(tenant, site, name, publisher, version, asset));
    return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(output)).ToArray(), "text/csv; charset=utf-8", "DMT-computer-versions.csv");
});
app.Run();
