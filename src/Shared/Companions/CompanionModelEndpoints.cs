namespace Mir2.Companions;

public static class CompanionModelEndpoints
{
    public static string Chat(string value) => Endpoint(value, "chat/completions");
    public static string Models(string value) => Endpoint(value, "models");

    private static string Endpoint(string value, string resource)
    {
        if (value == null || value.Length > 2048 || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidDataException("API地址须为不含内嵌凭据或片段的完整 http/https 地址。");
        var path = uri.AbsolutePath.TrimEnd('/');
        foreach (var suffix in new[] { "/chat/completions", "/models" })
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { path = path[..^suffix.Length]; break; }
        if (path.Length == 0) path = "/v1";
        return new UriBuilder(uri) { Path = path + "/" + resource }.Uri.AbsoluteUri;
    }
}
