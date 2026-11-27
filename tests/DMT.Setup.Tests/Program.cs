using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using DMT.Inventory;
using DMT.Setup.Services;

var assertions = 0;
assertions += InstallationProgressTests.Run();
void Check(bool value, string name) { if (!value) throw new Exception(name); assertions++; }
var credential = LocalAdminCredential.Create("test-admin", "Synthetic-password-123!");
Check(credential.Verify("TEST-ADMIN", "Synthetic-password-123!"), "password_case_insensitive_username");
Check(!credential.Verify("test-admin", "wrong"), "wrong_password");
Check(!credential.Verify("other", "Synthetic-password-123!"), "wrong_username");
Check(LocalAdminCredential.Create("test-admin", "Synthetic-password-123!").Salt != credential.Salt, "random_password_salt");
Check(!(credential with { Hash = "invalid" }).Verify("test-admin", "Synthetic-password-123!"), "invalid_hash_rejected");
Check(LocalAdminCredential.Create("test-admin", "Eight123").Verify("test-admin", "Eight123"), "eight_character_local_password");
var shortPasswordRejected = false;
try { LocalAdminCredential.Create("test-admin", "Seven12"); } catch (ArgumentException) { shortPasswordRejected = true; }
Check(shortPasswordRejected, "seven_character_password_rejected");
var folder = Path.Combine(Path.GetTempPath(), "DMT-setup-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder); Console.WriteLine("temporary_directory=" + folder);
Process? backend = null;
Task<string>? capturedOut = null, capturedError = null;
var testFailed = true;
try
{
    Check(LocalServiceDeployment.Contained(folder, @"Service\app.exe").StartsWith(folder), "payload_containment");
    foreach (var path in new[] { @"..\escape.exe", @"C:\escape.exe" })
    {
        var rejected = false;
        try { LocalServiceDeployment.Contained(folder, path); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "unsafe_payload_path");
    }
    if (args.Length < 2) throw new ArgumentException("Supply web host DLL and Caddy executable for HTTPS integration verification");
    int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    var https = FreePort(); var http = FreePort(); while (http == https) http = FreePort();
    var db = Path.Combine(folder, "dmt.sqlite"); _ = new InventoryStore(db);
    var admin = Path.Combine(folder, "admin.json"); File.WriteAllText(admin, JsonSerializer.Serialize(credential));
    var caddy = Path.Combine(folder, "Caddyfile");
    File.WriteAllText(caddy, $"{{\n admin off\n auto_https disable_redirects\n skip_install_trust\n}}\nhttps://localhost:{https} {{\n bind 127.0.0.1\n tls internal\n reverse_proxy 127.0.0.1:{http}\n}}\n");
    var runtime = Path.Combine(folder, "runtime.json");
    File.WriteAllText(runtime, JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["Dmt:Installed"]="true",["Dmt:Database"]=db,["Dmt:AdminFile"]=admin,["Dmt:Instance"]="synthetic-test",
        ["Dmt:CaddyExecutable"]=Path.GetFullPath(args[1]),["Dmt:CaddyConfig"]=caddy,["Dmt:CaddyStorage"]=folder,["urls"]=$"http://127.0.0.1:{http}"
    }));
    var executableHost = args[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    var start = new ProcessStartInfo(executableHost ? Path.GetFullPath(args[0]) : "dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    if (!executableHost) start.ArgumentList.Add(Path.GetFullPath(args[0]));
    start.ArgumentList.Add("--Dmt:RuntimeConfig=" + runtime);
    backend = Process.Start(start) ?? throw new Exception("test_backend_launch");
    var stdout = backend.StandardOutput.ReadToEndAsync(); var stderr = backend.StandardError.ReadToEndAsync();
    capturedOut = stdout; capturedError = stderr;
    var rootPath = Path.Combine(folder, "caddy", "pki", "authorities", "local", "root.crt");
    var deadline = DateTime.UtcNow.AddSeconds(45);
    while (!File.Exists(rootPath) && DateTime.UtcNow < deadline && !backend.HasExited) await Task.Delay(200);
    if (!File.Exists(rootPath)) throw new Exception("test_caddy_start_failed: " + (backend.HasExited ? await stdout + await stderr : "timeout"));
    using var root = X509CertificateLoader.LoadCertificateFromFile(rootPath);
    using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
    handler.ServerCertificateCustomValidationCallback = (_, certificate, presentedChain, errors) =>
    {
        if (certificate == null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch)) return false;
        using var chain = new X509Chain(); chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root); chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        if (presentedChain != null) foreach (var element in presentedChain.ChainElements) chain.ChainPolicy.ExtraStore.Add(element.Certificate);
        return chain.Build(certificate);
    };
    using var client = new HttpClient(handler) { BaseAddress = new Uri($"https://localhost:{https}"), Timeout = TimeSpan.FromSeconds(10) };
    HttpResponseMessage? loginPage = null;
    while (DateTime.UtcNow < deadline)
    {
        try { loginPage = await client.GetAsync("/login"); if (loginPage.IsSuccessStatusCode) break; loginPage.Dispose(); }
        catch (HttpRequestException) { }
        await Task.Delay(200);
    }
    Check(loginPage?.StatusCode == HttpStatusCode.OK, "real_caddy_https_login_page: " + loginPage?.StatusCode); loginPage?.Dispose();
    Check((await client.GetAsync("/api/systems")).StatusCode == HttpStatusCode.Unauthorized, "inventory_api_requires_login");
    Check((await client.GetAsync("/")).StatusCode == HttpStatusCode.Found, "dashboard_redirects_to_login");
    Check((await client.PostAsJsonAsync("/auth/login", new { username="test-admin", password="Synthetic-password-123!" })).StatusCode == HttpStatusCode.BadRequest, "login_csrf_required");
    async Task<string> Token()
    { using var json = JsonDocument.Parse(await client.GetStringAsync("/auth/token")); return json.RootElement.GetProperty("token").GetString()!; }
    var token = await Token();
    using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login") { Content = JsonContent.Create(new { username="test-admin", password="Synthetic-password-123!" }) };
    request.Headers.Add("X-DMT-CSRF", token);
    using var response = await client.SendAsync(request);
    Check(response.StatusCode == HttpStatusCode.OK, "login_through_https");
    Check(response.Headers.GetValues("Set-Cookie").Any(v => v.Contains("__Host-DMT=") && v.Contains("secure") && v.Contains("httponly")), "secure_auth_cookie");
    Check((await client.GetAsync("/api/systems")).StatusCode == HttpStatusCode.OK, "authenticated_inventory_api");
    using var logout = new HttpRequestMessage(HttpMethod.Post, "/auth/logout"); logout.Headers.Add("X-DMT-CSRF", await Token());
    Check((await client.SendAsync(logout)).StatusCode == HttpStatusCode.OK, "csrf_protected_logout");
    Check((await client.GetAsync("/api/systems")).StatusCode == HttpStatusCode.Unauthorized, "logout_revokes_session");
    backend.Kill(entireProcessTree: false); await backend.WaitForExitAsync();
    var childStopped = false;
    for (var i=0;i<30;i++)
    {
        using var socket = new TcpClient();
        try { await socket.ConnectAsync(IPAddress.Loopback, https); await Task.Delay(100); }
        catch (SocketException) { childStopped = true; break; }
    }
    Check(childStopped, "caddy_owned_job_stops_on_parent_crash");
    testFailed = false;
    Console.WriteLine("PASS setup assertions=" + assertions);
}
finally
{
    if (backend != null) { if (!backend.HasExited) { backend.Kill(entireProcessTree: true); await backend.WaitForExitAsync(); } backend.Dispose(); }
    if (testFailed) { if (capturedOut != null) Console.WriteLine(await capturedOut); if (capturedError != null) Console.WriteLine(await capturedError); }
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    if (!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(folder).StartsWith("DMT-setup-tests-")) throw new Exception("unsafe_test_cleanup");
    Directory.Delete(folder, recursive: true); Console.WriteLine("temporary_directory_removed=" + folder);
}
