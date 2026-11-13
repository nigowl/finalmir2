#if DEBUG
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Mir2.Admin.Services;

internal static class LocalDevelopmentAuthentication
{
    public static void UseLocalDevelopmentAuthentication(this WebApplication app) => app.Use(async (context, next) =>
    {
        var request = context.Request;
        var local = context.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);
        var origin = request.Headers.Origin.ToString();
        var sameOrigin = origin.Length == 0 || Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && uri.Scheme == request.Scheme && uri.Authority.Equals(request.Host.Value, StringComparison.OrdinalIgnoreCase);
        // 免密仅供本机调试，跨站请求不能借用开发身份操作当前版本。
        if (!local || !sameOrigin || request.Headers["Sec-Fetch-Site"] == "cross-site")
        { context.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "本机开发")], CookieAuthenticationDefaults.AuthenticationScheme));
        if (request.Path.Equals("/login", StringComparison.OrdinalIgnoreCase)) { context.Response.Redirect("/"); return; }
        await next();
    });
}
#endif
