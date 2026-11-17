using System.Security.Claims;
using System.Text.Json;
using DMT.Inventory;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;

namespace DMT.Web;

public static class LocalAuthentication
{
    public static void Configure(WebApplicationBuilder builder)
    {
        var keyDirectory = Path.Combine(Path.GetDirectoryName(builder.Configuration["Dmt:Database"])!, "keys");
        var protection = builder.Services.AddDataProtection().SetApplicationName("DMT").PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
        if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi(protectToLocalMachine: true);
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.LoginPath = "/login"; options.Cookie.Name = "__Host-DMT";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always; options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.HttpOnly = true; options.ExpireTimeSpan = TimeSpan.FromHours(8); options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = 401;
                else context.Response.Redirect("/login");
                return Task.CompletedTask;
            };
            options.Events.OnValidatePrincipal = context =>
            {
                try
                {
                    var current = JsonSerializer.Deserialize<LocalAdminCredential>(File.ReadAllText(builder.Configuration["Dmt:AdminFile"]!));
                    if (current == null || context.Principal?.FindFirst("dmt_stamp")?.Value != Stamp(current)) context.RejectPrincipal();
                }
                catch (Exception e) when (e is IOException or JsonException) { context.RejectPrincipal(); }
                return Task.CompletedTask;
            };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery(o => { o.HeaderName = "X-DMT-CSRF"; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Strict; });
        builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("login", limiter =>
        { limiter.PermitLimit = 5; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; limiter.AutoReplenishment = true; }));
    }
    public static void Map(WebApplication app, string credentialsPath)
    {
        var credentials = JsonSerializer.Deserialize<LocalAdminCredential>(File.ReadAllText(credentialsPath)) ?? throw new InvalidDataException("admin_credentials_missing");
        app.MapGet("/auth/session", () => Results.Ok(new { enabled = true }));
        app.MapGet("/login", () => Results.File(Path.Combine(AppContext.BaseDirectory, "wwwroot", "login.html"), "text/html; charset=utf-8"));
        app.MapGet("/login.js", () => Results.File(Path.Combine(AppContext.BaseDirectory, "wwwroot", "login.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/auth/token", (HttpContext context, IAntiforgery antiforgery) => Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
        app.MapPost("/auth/login", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); } catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            if (context.Request.ContentLength is > 8192) return Results.BadRequest();
            LoginRequest? request;
            try { request = await context.Request.ReadFromJsonAsync<LoginRequest>(); } catch (JsonException) { return Results.BadRequest(); }
            if (request == null || !credentials.Verify(request.Username ?? "", request.Password ?? "")) return Results.Unauthorized();
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, credentials.Username), new Claim("dmt_stamp", Stamp(credentials))], CookieAuthenticationDefaults.AuthenticationScheme)));
            return Results.Ok();
        }).RequireRateLimiting("login");
        app.MapPost("/auth/logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); } catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            await context.SignOutAsync(); return Results.Ok();
        }).RequireAuthorization();
    }
    private sealed record LoginRequest(string? Username, string? Password);
    private static string Stamp(LocalAdminCredential credential) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(credential.Hash)));
}
