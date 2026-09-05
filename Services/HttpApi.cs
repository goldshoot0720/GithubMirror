using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GithubMirror.Services;

internal static class HttpApi
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        Timeout = TimeSpan.FromSeconds(90)
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static Task<JsonDocument> SendBearerAsync(HttpMethod method, string url, string token, object? body = null, CancellationToken cancellationToken = default) =>
        SendAsync(method, url, request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token), body, cancellationToken);

    public static Task<JsonDocument> SendBasicAsync(HttpMethod method, string url, string username, string password, object? body = null, CancellationToken cancellationToken = default) =>
        SendAsync(method, url, request =>
        {
            var value = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", value);
        }, body, cancellationToken);

    public static Task<JsonDocument> SendPrivateTokenAsync(HttpMethod method, string url, string token, object? body = null, CancellationToken cancellationToken = default) =>
        SendAsync(method, url, request => request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token), body, cancellationToken);

    public static Task<JsonDocument> SendGiteaTokenAsync(HttpMethod method, string url, string token, object? body = null, CancellationToken cancellationToken = default) =>
        SendAsync(method, url, request => request.Headers.Authorization = new AuthenticationHeaderValue("token", token), body, cancellationToken);

    private static async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string url,
        Action<HttpRequestMessage> authorize,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("GithubMirror/1.0");
        authorize(request);

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ApiRequestException(response.StatusCode, ExtractMessage(content));

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
    }

    private static string ExtractMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "伺服器未提供錯誤內容";
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            foreach (var key in new[] { "message", "error_description", "error", "detail" })
            {
                if (root.TryGetProperty(key, out var value)) return value.ToString();
            }
        }
        catch (JsonException)
        {
            // Return a short plain-text error below.
        }

        return content.Length > 500 ? content[..500] : content;
    }
}

internal sealed class ApiRequestException(HttpStatusCode statusCode, string message)
    : InvalidOperationException($"API 回應 {(int)statusCode} ({statusCode})：{message}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

internal static class JsonExtensions
{
    public static string String(this JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : string.Empty;

    public static long Int64(this JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;
    }

    public static int Int32(this JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
    }

    public static bool Boolean(this JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    public static DateTimeOffset Date(this JsonElement element, string property)
    {
        var text = element.String(property);
        return DateTimeOffset.TryParse(text, out var value) ? value : DateTimeOffset.MinValue;
    }
}
