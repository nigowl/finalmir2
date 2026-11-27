using System.Text.Json;
using Mir2.Companions;

namespace Server.ConsoleHost;

public sealed partial class CompanionService
{
    private readonly HttpClient modelDiscovery = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<string, DateTimeOffset> lastModelDiscovery = [];

    public async Task<CompanionModelList> DiscoverModels(CompanionModelDiscovery request, CancellationToken token)
    {
        if (request == null || request.Key?.Length > 4096) throw new InvalidDataException("API地址或密钥无效。");
        var endpoint = CompanionModelEndpoints.Models(request.Endpoint);
        if (!await modelTests.WaitAsync(0, token)) throw new InvalidOperationException("已有两个模型请求进行中，请稍后重试。");
        try
        {
            var key = await runtime.Invoke(() =>
            {
                store.Ready();
                var now = DateTimeOffset.UtcNow;
                if (lastModelDiscovery.GetValueOrDefault(endpoint) > now) throw new InvalidOperationException("同一服务商请间隔5秒再获取模型。");
                foreach (var old in lastModelDiscovery.Where(p => p.Value <= now).Select(p => p.Key).ToArray()) lastModelDiscovery.Remove(old);
                if (lastModelDiscovery.Count >= 100) throw new InvalidOperationException("模型列表请求过多，请稍后重试。");
                lastModelDiscovery[endpoint] = now.AddSeconds(5);
                return request.ClearKey ? "" : request.Key ?? CompanionModels.SavedKey(store.Configuration, request.ModelId, request.Endpoint);
            }, token);
            if (key.Any(char.IsControl)) throw new InvalidDataException("API密钥不能含有换行或控制字符。");
            using var message = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (key.Length > 0) message.Headers.Authorization = new("Bearer", key);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await modelDiscovery.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var bytes = new MemoryStream(); var buffer = new byte[4096]; int read;
            while ((read = await stream.ReadAsync(buffer, deadline.Token)) > 0)
            {
                if (bytes.Length + read > 524288) throw new InvalidDataException("模型列表超过512KB，请缩小服务商返回范围。");
                bytes.Write(buffer, 0, read);
            }
            using var document = JsonDocument.Parse(bytes.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("服务商未返回兼容的data模型列表，请改为手动填写模型标识。");
            var models = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) continue;
                var name = id.GetString()!;
                if (name.Length is < 1 or > 200 || name != name.Trim() || name.Any(char.IsControl)) continue;
                models.Add(name);
                if (models.Count > 1000) throw new InvalidDataException("服务商返回超过1000个模型，请手动填写所需模型标识。");
            }
            return new(CompanionModelEndpoints.Chat(request.Endpoint), models.Order(StringComparer.Ordinal).ToArray());
        }
        catch (HttpRequestException error)
        { throw new InvalidOperationException(error.StatusCode is { } status ? $"获取模型失败：HTTP {(int)status}，请检查API地址、密钥及权限。" : "无法连接模型服务商，请检查网络、地址和证书。"); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new InvalidOperationException("获取模型超时，请稍后重试。"); }
        catch (JsonException) { throw new InvalidDataException("服务商返回的模型列表不是有效JSON。"); }
        finally { modelTests.Release(); }
    }
}
