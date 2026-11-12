using System.Net.Http.Json;
using System.Text.Json;

namespace Mir2.Admin.Services;

public sealed class AdminApi(HttpClient client)
{
    public static string Escape(string value) => string.Join('/', value.Split('/').Select(Uri.EscapeDataString));
    private static async Task Ensure(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync();
        try { detail = JsonDocument.Parse(detail).RootElement.GetProperty("error").GetString() ?? detail; } catch (Exception) { }
        throw new HttpRequestException($"请求失败（{(int)response.StatusCode}）：{detail}");
    }
    public async Task<T> Get<T>(string path, CancellationToken token = default)
    {
        using var response = await client.GetAsync(path, token); await Ensure(response);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: token) ?? throw new InvalidDataException("服务端返回内容为空。");
    }
    public async Task<T> Post<T>(string path, object? value = null)
    {
        using var response = value == null ? await client.PostAsync(path, null) : await client.PostAsJsonAsync(path, value); await Ensure(response);
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidDataException("服务端返回内容为空。");
    }
    public async Task<T> Put<T>(string path, object value)
    {
        using var response = await client.PutAsJsonAsync(path, value); await Ensure(response);
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidDataException("服务端返回内容为空。");
    }
    public async Task<IResult> Proxy(string path, HttpContext context, string type, string? download = null)
    {
        var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        if (!response.IsSuccessStatusCode) { var code = (int)response.StatusCode; response.Dispose(); return Results.StatusCode(code); }
        context.Response.RegisterForDispose(response);
        return Results.Stream(await response.Content.ReadAsStreamAsync(context.RequestAborted), type, download);
    }
}
