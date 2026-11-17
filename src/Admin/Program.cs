using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Mir2.Admin.Components;
using Mir2.Admin.Services;
using Mir2.Hosting;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
var paths = new ProjectPaths(versionDirectory: builder.Configuration["Mir2:VersionDirectory"]); var key = paths.AdminKey();
var cookieScope = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(paths.Runtime)))[..12];
builder.WebHost.UseStaticWebAssets();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls($"http://127.0.0.1:{Environment.GetEnvironmentVariable("MIR2_ADMIN_PORT") ?? "5081"}");
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddRazorPages();
builder.Services.AddMudServices(); builder.Services.AddTransient<MudBlazor.MudLocalizer, ChineseMudLocalizer>();
builder.Services.AddCascadingAuthenticationState(); builder.Services.AddAuthorization();
builder.Services.AddSingleton(paths);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/login"; options.Cookie.Name = "Mir2.Admin." + cookieScope; options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict; options.ExpireTimeSpan = TimeSpan.FromHours(8);
});
builder.Services.AddAntiforgery(options => options.Cookie.Name = "Mir2.Admin.Antiforgery." + cookieScope);
var keys = Path.Combine(paths.Runtime, "admin-keys"); Directory.CreateDirectory(keys);
if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keys, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keys)).SetApplicationName("Mir2.Admin." + cookieScope);
builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("login", limiter =>
{ limiter.PermitLimit = 10; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; }));
builder.Services.AddHttpClient<AdminApi>(client =>
{
    client.BaseAddress = new Uri((Environment.GetEnvironmentVariable("MIR2_SERVER") ?? builder.Configuration["Mir2:ServerUrl"] ?? "http://127.0.0.1:5080").TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Authorization = new("Bearer", key); client.Timeout = TimeSpan.FromMinutes(3);
    client.DefaultRequestHeaders.Add("X-Mir2-Version", paths.VersionId);
});
var app = builder.Build();
app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("zh-CN").AddSupportedCultures("zh-CN").AddSupportedUICultures("zh-CN"));
app.Use(async (context, next) =>
{
    var host = context.Request.Host.Host;
    if (host != "localhost" && (!IPAddress.TryParse(host, out var ip) || !IPAddress.IsLoopback(ip))) { context.Response.StatusCode = 403; return; }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY"; await next();
});
app.UseStaticFiles(); app.UseAuthentication();
#if DEBUG
if (app.Environment.IsDevelopment()) app.UseLocalDevelopmentAuthentication();
#endif
app.UseAuthorization(); app.UseRateLimiter(); app.UseAntiforgery();
app.MapPost("/auth/login", async (HttpContext context, IAntiforgery antiforgery) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest("表单验证失败，请刷新后重试。"); }
    var form = await context.Request.ReadFormAsync();
    if (!ProjectPaths.KeyEquals(form["key"], key)) return Results.Redirect("/login?error=1");
    await context.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Administrator")], CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Redirect("/");
}).RequireRateLimiting("login").AllowAnonymous();
app.MapPost("/auth/logout", async (HttpContext context, IAntiforgery antiforgery) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest("表单验证失败，请刷新后重试。"); }
    await context.SignOutAsync(); return Results.Redirect("/login");
}).RequireAuthorization();
app.MapGet("/asset-preview", async (string library, int index, AdminApi api, HttpContext context) =>
    await api.Proxy($"management/resources/preview/{AdminApi.Escape(library)}?index={index}", context, "image/png")).RequireAuthorization();
app.MapGet("/resource-map-preview", async (string name, AdminApi api, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return await api.Proxy("management/resources/map-thumbnail/" + AdminApi.Escape(name), context, "image/png");
}).RequireAuthorization();
app.MapGet("/native-map-tile", async (int id, string version, int x, int y, AdminApi api, HttpContext context) =>
    await api.Proxy($"management/admin/maps/{id}/tile?version={Uri.EscapeDataString(version)}&x={x}&y={y}", context, "image/png")).RequireAuthorization();
app.MapGet("/map-thumbnail-version/{id:int}", async (int id, AdminApi api, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Json(await api.Get<System.Text.Json.JsonElement>($"management/admin/maps/{id}/thumbnail"));
}).RequireAuthorization();
app.MapGet("/map-preview/{id:int}", async (int id, string version, AdminApi api, HttpContext context) =>
    await api.Proxy($"management/admin/maps/{id}/image?version={Uri.EscapeDataString(version)}", context, "image/png")).RequireAuthorization();
app.MapGet("/asset-download/{kind}/{**name}", async (string kind, string name, AdminApi api, HttpContext context) =>
    await api.Proxy($"management/resources/download/{AdminApi.Escape(kind)}/{AdminApi.Escape(name)}", context, "application/octet-stream", Path.GetFileName(name))).RequireAuthorization();
app.MapGet("/health", () => new { ready = true, service = "Mir2.Admin", gameVersion = paths.VersionName, versionDirectory = paths.Data });
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
Console.WriteLine($"Game version: {paths.VersionName}\nVersion directory: {paths.Data}\nManagement: http://127.0.0.1:{Environment.GetEnvironmentVariable("MIR2_ADMIN_PORT") ?? "5081"}\nLogin key file: {paths.AdminKeyFile}");
app.Run();
