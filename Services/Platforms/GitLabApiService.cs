using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GithubMirror.Models;

namespace GithubMirror.Services.Platforms;

public sealed class GitLabApiService : IGitService
{
    public string Platform => PlatformIds.GitLab;

    public async Task<ProbeResult> ProbeAsync(AccountCredential credential, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(credential.Token)) return ProbeResult.Fail("請輸入 GitLab Token。");
        try
        {
            using var request = Rest.Get($"{ApiBase(credential)}/user");
            Authorize(request, credential.Token);
            using var json = await Rest.SendJsonAsync(request, "驗證 GitLab Token", ct).ConfigureAwait(false);
            var username = Rest.Str(json.RootElement, "username");
            return string.IsNullOrWhiteSpace(username)
                ? ProbeResult.Fail("GitLab 未回傳使用者名稱。")
                : ProbeResult.Ok(username, Rest.Str(json.RootElement, "name", username));
        }
        catch (GitPlatformException ex) { return ProbeResult.Fail(ex.Message); }
    }

    public async Task<IReadOnlyList<RepositoryInfo>> ListRepositoriesAsync(
        AccountCredential credential, IProgress<string>? progress, CancellationToken ct)
    {
        var result = new List<RepositoryInfo>();
        for (var page = 1; ; page++)
        {
            progress?.Report($"正在讀取 GitLab 專案（第 {page} 頁）…");
            using var request = Rest.Get($"{ApiBase(credential)}/projects?membership=true&statistics=true&per_page=100&page={page}&order_by=updated_at&sort=desc");
            Authorize(request, credential.Token);
            using var json = await Rest.SendJsonAsync(request, "讀取 GitLab 專案", ct).ConfigureAwait(false);
            var items = json.RootElement;
            foreach (var item in items.EnumerateArray()) result.Add(Map(item));
            if (items.GetArrayLength() < 100) break;
        }
        progress?.Report($"GitLab 讀取完成，共 {result.Count} 個專案。");
        return result;
    }

    public Task<string> BuildSourceUrlAsync(RepositoryInfo repo, AccountCredential credential, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(GitHelper.InjectAuth(repo.CloneUrl, "oauth2", credential.Token));
    }

    public async Task<TargetRepository> EnsureTargetAsync(
        AccountCredential credential, RepositoryInfo source, string targetName, bool isPrivate, CancellationToken ct)
    {
        targetName = GitHelper.SanitizeRepoName(targetName);
        var api = ApiBase(credential);
        long? namespaceId = null;
        if (!string.IsNullOrWhiteSpace(credential.Organization))
        {
            if (!long.TryParse(credential.Organization, out var parsed))
                throw new GitPlatformException("GitLab 目標 namespace 必須填數字 ID。");
            namespaceId = parsed;
        }

        using var create = Rest.PostJson($"{api}/projects", new
        {
            name = targetName,
            path = targetName,
            namespace_id = namespaceId,
            description = string.IsNullOrWhiteSpace(source.Description) ? $"Mirror of {source.FullName}" : source.Description,
            visibility = isPrivate ? "private" : "public"
        });
        Authorize(create, credential.Token);
        var response = await Rest.TrySendAsync(create, ct).ConfigureAwait(false);
        var wasCreated = response.Ok;
        JsonDocument json;
        if (response.Ok)
        {
            json = PlatformHelpers.Parse(response.Body, "建立 GitLab project");
        }
        else if (response.Status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            var owner = string.IsNullOrWhiteSpace(credential.Organization) ? credential.Username : await ResolveNamespacePathAsync(api, credential, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(owner)) throw new GitPlatformException("無法判斷 GitLab 目標 namespace。");
            using var existing = Rest.Get($"{api}/projects/{Uri.EscapeDataString($"{owner}/{targetName}")}");
            Authorize(existing, credential.Token);
            json = await Rest.SendJsonAsync(existing, "取得既有 GitLab project", ct).ConfigureAwait(false);
        }
        else throw PlatformHelpers.RequestFailed("建立 GitLab project", response.Status, response.Body);

        using (json)
        {
            var url = Rest.Str(json.RootElement, "http_url_to_repo");
            if (string.IsNullOrWhiteSpace(url)) throw new GitPlatformException("GitLab 未回傳 Clone URL。");
            return new TargetRepository
            {
                PushUrl = GitHelper.InjectAuth(url, "oauth2", credential.Token),
                WebUrl = Rest.Str(json.RootElement, "web_url"),
                WasCreated = wasCreated
            };
        }
    }

    private static async Task<string> ResolveNamespacePathAsync(string api, AccountCredential credential, CancellationToken ct)
    {
        using var request = Rest.Get($"{api}/namespaces/{Uri.EscapeDataString(credential.Organization)}");
        Authorize(request, credential.Token);
        using var json = await Rest.SendJsonAsync(request, "取得 GitLab namespace", ct).ConfigureAwait(false);
        return Rest.Str(json.RootElement, "full_path", Rest.Str(json.RootElement, "path"));
    }

    private static RepositoryInfo Map(JsonElement item)
    {
        var fullPath = Rest.Str(item, "path_with_namespace");
        var slash = fullPath.LastIndexOf('/');
        long? size = null;
        if (item.TryGetProperty("statistics", out var statistics)) size = Rest.Long(statistics, "repository_size");
        return new RepositoryInfo
        {
            Name = Rest.Str(item, "name"),
            Owner = slash > 0 ? fullPath[..slash] : string.Empty,
            NativeId = Rest.Long(item, "id")?.ToString() ?? string.Empty,
            CloneUrl = Rest.Str(item, "http_url_to_repo"),
            WebUrl = Rest.Str(item, "web_url"),
            Description = Rest.Str(item, "description"),
            DefaultBranch = Rest.Str(item, "default_branch"),
            SizeInBytes = size,
            Stars = Rest.Int(item, "star_count"),
            Forks = Rest.Int(item, "forks_count"),
            IsPrivate = !string.Equals(Rest.Str(item, "visibility"), "public", StringComparison.OrdinalIgnoreCase),
            LastUpdated = Rest.Date(item, "last_activity_at"),
            Topics = Rest.StringList(item, "topics")
        };
    }

    private static string ApiBase(AccountCredential credential)
    {
        var root = (string.IsNullOrWhiteSpace(credential.ApiUrl) ? "https://gitlab.com" : credential.ApiUrl).TrimEnd('/');
        return root.EndsWith("/api/v4", StringComparison.OrdinalIgnoreCase) ? root : $"{root}/api/v4";
    }

    private static void Authorize(HttpRequestMessage request, string token) =>
        request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);
}
