using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GithubMirror.Services;

/// <summary>REST 呼叫的共用小工具，統一逾時、錯誤訊息與 JSON 解析。</summary>
public static class Rest
{
    public static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GithubMirror/1.0");
        return client;
    }

    public static HttpRequestMessage Get(string url) => new(HttpMethod.Get, url);

    public static HttpRequestMessage PostJson(string url, object payload)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        return req;
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static void UseBearer(HttpRequestMessage req, string token)
        => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public static void UseBasic(HttpRequestMessage req, string user, string password)
    {
        var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", raw);
    }

    /// <summary>送出請求並在失敗時丟出帶有可讀訊息的例外。</summary>
    public static async Task<JsonDocument> SendJsonAsync(
        HttpRequestMessage request, string what, CancellationToken ct)
    {
        var body = await SendStringAsync(request, what, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
            return JsonDocument.Parse("{}");

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new GitPlatformException($"{what}：回應不是合法 JSON（{ex.Message}）");
        }
    }

    public static async Task<string> SendStringAsync(
        HttpRequestMessage request, string what, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GitPlatformException($"{what}：連線逾時。");
        }
        catch (HttpRequestException ex)
        {
            throw new GitPlatformException($"{what}：無法連線（{ex.Message}）");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return body;

            throw new GitPlatformException(DescribeError(what, response.StatusCode, body));
        }
    }

    /// <summary>送出請求並回傳 (是否成功, 狀態碼, 內容)，讓呼叫端自行處理 409/422 等。</summary>
    public static async Task<(bool Ok, HttpStatusCode Status, string Body)> TrySendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var response = await Client.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (response.IsSuccessStatusCode, response.StatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return (false, HttpStatusCode.ServiceUnavailable, ex.Message);
        }
    }

    private static string DescribeError(string what, HttpStatusCode status, string body)
    {
        var detail = ExtractMessage(body);
        var friendly = status switch
        {
            HttpStatusCode.Unauthorized => "Token 無效或已過期",
            HttpStatusCode.Forbidden => "Token 權限不足（或觸發速率限制）",
            HttpStatusCode.NotFound => "找不到資源（請確認網址 / 組織 / 專案名稱）",
            HttpStatusCode.Conflict => "資源已存在",
            (HttpStatusCode)422 => "請求被拒絕（名稱可能已被使用）",
            _ => $"HTTP {(int)status}"
        };

        return string.IsNullOrWhiteSpace(detail)
            ? $"{what}：{friendly}"
            : $"{what}：{friendly} — {detail}";
    }

    public static string ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(body);
            foreach (var key in new[] { "message", "error_description", "error", "detail", "errorMessages" })
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty(key, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.String)
                        return prop.GetString() ?? string.Empty;
                    return prop.ToString();
                }
            }
        }
        catch (JsonException)
        {
            // 不是 JSON，直接截斷原文
        }

        var trimmed = body.Trim();
        return trimmed.Length > 200 ? trimmed[..200] + "…" : trimmed;
    }

    // ---- JsonElement 讀取小工具 ----

    public static string Str(JsonElement e, string name, string fallback = "")
        => e.ValueKind == JsonValueKind.Object &&
           e.TryGetProperty(name, out var p) &&
           p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? fallback
            : fallback;

    public static long? Long(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p))
            return null;
        return p.ValueKind switch
        {
            JsonValueKind.Number when p.TryGetInt64(out var v) => v,
            JsonValueKind.String when long.TryParse(p.GetString(), out var v) => v,
            _ => null
        };
    }

    public static int Int(JsonElement e, string name, int fallback = 0)
        => (int)(Long(e, name) ?? fallback);

    public static bool Bool(JsonElement e, string name, bool fallback = false)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p))
            return fallback;
        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    public static DateTime? Date(JsonElement e, string name)
    {
        var s = Str(e, name);
        return DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt
            : null;
    }

    public static List<string> StringList(JsonElement e, string name)
    {
        var list = new List<string>();
        if (e.ValueKind == JsonValueKind.Object &&
            e.TryGetProperty(name, out var p) &&
            p.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in p.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrEmpty(s)) list.Add(s);
                }
            }
        }
        return list;
    }
}

/// <summary>平台 API 相關的可讀錯誤。</summary>
public class GitPlatformException : Exception
{
    public GitPlatformException(string message) : base(message) { }
    public GitPlatformException(string message, Exception inner) : base(message, inner) { }
}
