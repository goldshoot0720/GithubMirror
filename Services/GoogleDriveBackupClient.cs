using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GithubMirror.Services;

public sealed record DriveBackupFile(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed class GoogleDriveBackupClient : IDisposable
{
    private readonly HttpClient _http;
    private string? _accessToken;
    private DateTimeOffset _expiresAt;
    public bool IsConnected => _accessToken is not null && DateTimeOffset.UtcNow < _expiresAt;
    public GoogleDriveBackupClient() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }
    internal GoogleDriveBackupClient(HttpMessageHandler handler) => _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
    internal void SetSession(string token, int seconds)
    {
        _accessToken = token;
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, seconds - 60));
    }

    public async Task SignInAsync(string clientJson, CancellationToken ct)
    {
        _accessToken = null;
        using var doc = JsonDocument.Parse(clientJson);
        if (!doc.RootElement.TryGetProperty("installed", out var installed) ||
            !installed.TryGetProperty("client_id", out var id) || string.IsNullOrWhiteSpace(id.GetString()))
            throw new InvalidDataException("請匯入 Google OAuth「桌面應用程式」的 JSON 設定檔。");
        var clientId = id.GetString()!;
        var secret = installed.TryGetProperty("client_secret", out var value) ? value.GetString() : null;
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["redirect_uri"] = redirect, ["response_type"] = "code",
            ["scope"] = "https://www.googleapis.com/auth/drive.file", ["state"] = state,
            ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["prompt"] = "select_account"
        };
        var url = "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&", parameters.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
        if (!SystemBrowser.TryOpen(url, out _)) throw new InvalidOperationException("無法開啟預設瀏覽器，請設定瀏覽器後重試。");
        string? code = null;
        while (code is null)
        {
            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            string? request;
            try
            {
                var line = new StringBuilder();
                var character = new char[1];
                while (line.Length < 8192 && await reader.ReadAsync(character.AsMemory(), requestTimeout.Token) > 0)
                {
                    if (character[0] == '\n') break;
                    line.Append(character[0]);
                }
                request = line.Length >= 8192 ? null : line.ToString().TrimEnd('\r');
            }
            catch (OperationCanceledException) when (!timeout.IsCancellationRequested) { continue; }
            var parts = request?.Split(' ');
            var query = new Dictionary<string, string>();
            if (parts?.Length == 3 && parts[0] == "GET" && parts[1].Length < 8192 && parts[1].StartsWith("/?", StringComparison.Ordinal))
            {
                foreach (var item in parts[1][2..].Split('&'))
                {
                    var pair = item.Split('=', 2);
                    if (pair.Length == 2) query[Uri.UnescapeDataString(pair[0])] = Uri.UnescapeDataString(pair[1].Replace("+", " "));
                }
            }
            var valid = query.TryGetValue("state", out var received) &&
                CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(received));
            var message = valid ? "Google authorization received. Return to GithubMirror." : "Invalid authorization request.";
            var body = Encoding.UTF8.GetBytes(message);
            var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {(valid ? "200 OK" : "400 Bad Request")}\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: {body.Length}\r\n\r\n");
            await stream.WriteAsync(response, timeout.Token);
            await stream.WriteAsync(body, timeout.Token);
            if (!valid) continue;
            if (query.ContainsKey("error")) throw new InvalidOperationException("Google 授權未完成；請重新登入並允許備份檔案存取。");
            if (query.TryGetValue("code", out var returnedCode) && !string.IsNullOrWhiteSpace(returnedCode)) code = returnedCode;
        }
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["code"] = code, ["code_verifier"] = verifier,
            ["redirect_uri"] = redirect, ["grant_type"] = "authorization_code"
        };
        if (!string.IsNullOrEmpty(secret)) form["client_secret"] = secret;
        using var content = new FormUrlEncodedContent(form);
        using var tokenResponse = await _http.PostAsync("https://oauth2.googleapis.com/token", content, timeout.Token);
        if (!tokenResponse.IsSuccessStatusCode) throw new InvalidOperationException("Google 登入失敗，請檢查 OAuth 桌面用戶端、測試使用者及系統時間後重試。");
        using var tokenDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(timeout.Token));
        SetSession(tokenDoc.RootElement.GetProperty("access_token").GetString()!, tokenDoc.RootElement.GetProperty("expires_in").GetInt32());
    }

    public async Task<string> UploadAsync(byte[] encrypted, CancellationToken ct)
    {
        var name = $"GithubMirror-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.gmbak";
        using var multipart = new MultipartContent("related");
        multipart.Add(new StringContent(JsonSerializer.Serialize(new
        {
            name, appProperties = new Dictionary<string, string> { ["githubMirrorBackup"] = "1" }
        }), Encoding.UTF8, "application/json"));
        var media = new ByteArrayContent(encrypted);
        media.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(media);
        using var request = Request(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,name");
        request.Content = multipart;
        using var response = await _http.SendAsync(request, ct);
        await CheckResponseAsync(response, ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("name").GetString()!;
    }

    public async Task<List<DriveBackupFile>> ListAsync(CancellationToken ct)
    {
        var files = new List<DriveBackupFile>();
        string? page = null;
        do
        {
            var q = "trashed = false and appProperties has { key='githubMirrorBackup' and value='1' }";
            var url = "https://www.googleapis.com/drive/v3/files?pageSize=100&orderBy=createdTime%20desc&fields=nextPageToken,files(id,name)&q=" + Uri.EscapeDataString(q);
            if (page is not null) url += "&pageToken=" + Uri.EscapeDataString(page);
            using var request = Request(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct);
            await CheckResponseAsync(response, ct);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            foreach (var file in doc.RootElement.GetProperty("files").EnumerateArray())
                files.Add(new DriveBackupFile(file.GetProperty("id").GetString()!, file.GetProperty("name").GetString()!));
            page = doc.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        } while (!string.IsNullOrEmpty(page));
        return files;
    }

    public async Task<byte[]> DownloadAsync(string id, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, "https://www.googleapis.com/drive/v3/files/" + Uri.EscapeDataString(id) + "?alt=media");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await CheckResponseAsync(response, ct);
        if (response.Content.Headers.ContentLength > EncryptedBackup.MaximumFileSize) throw new InvalidDataException("備份檔案過大。");
        using var input = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > EncryptedBackup.MaximumFileSize) throw new InvalidDataException("備份檔案過大。");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private HttpRequestMessage Request(HttpMethod method, string url)
    {
        if (!IsConnected) throw new InvalidOperationException("Google 登入已到期，請重新登入。");
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return request;
    }
    /// <summary>
    /// 失敗時把 Google 回傳的 error.reason / error.message 取出來，
    /// 換成使用者看得懂、知道下一步要做什麼的訊息，而不是只丟一個狀態碼。
    /// </summary>
    private async Task CheckResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.Unauthorized) _accessToken = null;

        var reason = string.Empty;
        var detail = string.Empty;

        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("error", out var error) &&
                    error.ValueKind == JsonValueKind.Object)
                {
                    if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                        detail = message.GetString() ?? string.Empty;

                    if (error.TryGetProperty("errors", out var errors) &&
                        errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0 &&
                        errors[0].TryGetProperty("reason", out var first) && first.ValueKind == JsonValueKind.String)
                        reason = first.GetString() ?? string.Empty;

                    if (reason.Length == 0 &&
                        error.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String)
                        reason = status.GetString() ?? string.Empty;
                }
            }
        }
        catch (Exception)
        {
            // 讀不到或不是 JSON，就退回一般說明
        }

        var advice = Advise(response.StatusCode, reason, detail);
        var reasonText = reason.Length > 0 ? reason : "未提供原因";
        var raw = detail.Length > 0
            ? $" Google 原始訊息：{(detail.Length > 300 ? detail[..300] + "…" : detail)}"
            : string.Empty;

        throw new InvalidOperationException(
            $"Google Drive 回應 {(int)response.StatusCode}（{reasonText}）。{advice}{raw}");
    }

    private static string Advise(HttpStatusCode status, string reason, string detail) => reason switch
    {
        "accessNotConfigured" =>
            "這個 Google Cloud 專案還沒啟用 Drive API。請到 Google Cloud →「API 和服務」→「程式庫」啟用「Google Drive API」，等 1～2 分鐘生效後再試。",
        "insufficientPermissions" or "insufficientFilePermissions" =>
            "登入時沒有取得雲端硬碟權限。請重新登入，並在 Google 同意畫面勾選允許存取 Google 雲端硬碟。",
        "appNotAuthorizedToFile" =>
            "這個檔案不是本程式建立的，目前的權限範圍讀不到。請改選本程式建立的備份。",
        "storageQuotaExceeded" =>
            "Google 雲端硬碟空間不足，請先清出空間再上傳。",
        "rateLimitExceeded" or "userRateLimitExceeded" =>
            "呼叫太頻繁被暫時限流，請等幾分鐘再試。",
        "dailyLimitExceeded" =>
            "已達今日 API 配額上限，請明天再試或到 Google Cloud 調高配額。",
        "authError" or "UNAUTHENTICATED" =>
            "登入已過期，請重新登入 Google 帳號。",
        _ when status == HttpStatusCode.Forbidden &&
               detail.Contains("has not been used in project", StringComparison.OrdinalIgnoreCase) =>
            "這個 Google Cloud 專案還沒啟用 Drive API，請先到 Google Cloud 啟用「Google Drive API」再試。",
        _ when status == HttpStatusCode.Forbidden =>
            "權限被拒。依常見程度依序檢查：① Google Cloud 專案是否已啟用 Drive API ② OAuth 同意畫面在「測試」模式下是否已把這個 Google 帳號加為測試使用者 ③ 登入時是否允許了雲端硬碟權限 ④ 雲端硬碟是否還有空間。",
        _ when status == HttpStatusCode.Unauthorized =>
            "登入已過期，請重新登入 Google 帳號。",
        _ when status == HttpStatusCode.NotFound =>
            "找不到這份備份，可能已被刪除。請按「重新整理備份清單」。",
        _ =>
            "請確認網路與 Google 帳號狀態；若上傳中斷，請先重新整理清單確認是否已建立備份，再決定要不要重試。"
    };
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public void Dispose()
    {
        _accessToken = null;
        _http.Dispose();
    }
}
