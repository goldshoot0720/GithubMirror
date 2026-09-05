using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GithubMirror.Models;

namespace GithubMirror.Services.Platforms;

public sealed class GitHubApiService : IGitService
{
    public string Platform => PlatformIds.GitHub;

    public async Task<ProbeResult> ProbeAsync(AccountCredential credential, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(credential.Token)) return ProbeResult.Fail("請輸入 GitHub Token。");
        try
        {
            using var request = Rest.Get($"{ApiBase(credential)}/user");
            Rest.UseBearer(request, credential.Token);
            using var json = await Rest.SendJsonAsync(request, "驗證 GitHub Token", ct).ConfigureAwait(false);
            var root = json.RootElement;
            var login = Rest.Str(root, "login");
            return string.IsNullOrWhiteSpace(login)
                ? ProbeResult.Fail("GitHub 未回傳使用者名稱。")
                : ProbeResult.Ok(login, Rest.Str(root, "name", login));
        }
        catch (GitPlatformException ex)
        {
            return ProbeResult.Fail(ex.Message);
        }
    }

    public async Task<IReadOnlyList<RepositoryInfo>> ListRepositoriesAsync(
        AccountCredential credential, IProgress<string>? progress, CancellationToken ct)
    {
        var result = new List<RepositoryInfo>();
        for (var page = 1; ; page++)
        {
            progress?.Report($"正在讀取 GitHub 專案（第 {page} 頁）…");
            using var request = Rest.Get(
                $"{ApiBase(credential)}/user/repos?per_page=100&page={page}&sort=updated&affiliation=owner,collaborator,organization_member");
            Rest.UseBearer(request, credential.Token);
            using var json = await Rest.SendJsonAsync(request, "讀取 GitHub 專案", ct).ConfigureAwait(false);
            var items = json.RootElement;
            if (items.ValueKind != JsonValueKind.Array) throw new GitPlatformException("GitHub 專案清單格式不正確。");
            foreach (var item in items.EnumerateArray()) result.Add(Map(item));
            if (items.GetArrayLength() < 100) break;
        }
        await GitTreeSize.FillLatestCommitSizesAsync(
            result,
            (repo, token) => FetchLatestCommitSizeAsync(credential, repo, token),
            progress,
            "正在讀取最近一次提交大小",
            ct).ConfigureAwait(false);
        progress?.Report($"GitHub 讀取完成，共 {result.Count} 個專案。");
        return result;
    }

    private async Task<long?> FetchLatestCommitSizeAsync(
        AccountCredential credential, RepositoryInfo repo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(repo.Owner) || string.IsNullOrWhiteSpace(repo.Name)) return null;
        var branch = string.IsNullOrWhiteSpace(repo.DefaultBranch) ? "HEAD" : repo.DefaultBranch;
        var url =
            $"{ApiBase(credential)}/repos/{Uri.EscapeDataString(repo.Owner)}/{Uri.EscapeDataString(repo.Name)}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1";
        return await GitTreeSize.FetchRecursiveTreeSizeAsync(url, request => Rest.UseBearer(request, credential.Token), ct)
            .ConfigureAwait(false);
    }

    public Task<string> BuildSourceUrlAsync(RepositoryInfo repo, AccountCredential credential, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(GitHelper.InjectAuth(repo.CloneUrl, "x-access-token", credential.Token));
    }

    public async Task<TargetRepository> EnsureTargetAsync(
        AccountCredential credential, RepositoryInfo source, string targetName, bool isPrivate, CancellationToken ct)
    {
        targetName = GitHelper.SanitizeRepoName(targetName);
        var api = ApiBase(credential);
        var owner = string.IsNullOrWhiteSpace(credential.Organization) ? credential.Username : credential.Organization;
        if (string.IsNullOrWhiteSpace(owner))
        {
            var probe = await ProbeAsync(credential, ct).ConfigureAwait(false);
            if (!probe.Success) throw new GitPlatformException(probe.Message);
            owner = probe.ResolvedUsername;
        }

        var createUrl = string.IsNullOrWhiteSpace(credential.Organization)
            ? $"{api}/user/repos"
            : $"{api}/orgs/{Uri.EscapeDataString(credential.Organization)}/repos";
        using var create = Rest.PostJson(createUrl, new
        {
            name = targetName,
            description = Description(source),
            @private = isPrivate,
            has_issues = true,
            has_wiki = true
        });
        Rest.UseBearer(create, credential.Token);
        var response = await Rest.TrySendAsync(create, ct).ConfigureAwait(false);
        var wasCreated = response.Ok;

        JsonDocument json;
        if (response.Ok)
        {
            json = Parse(response.Body, "建立 GitHub repository");
        }
        else if (response.Status is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            using var existing = Rest.Get($"{api}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(targetName)}");
            Rest.UseBearer(existing, credential.Token);
            json = await Rest.SendJsonAsync(existing, "取得既有 GitHub repository", ct).ConfigureAwait(false);
        }
        else
        {
            throw PlatformHelpers.RequestFailed("建立 GitHub repository", response.Status, response.Body);
        }

        using (json)
        {
            var clone = Rest.Str(json.RootElement, "clone_url");
            if (string.IsNullOrWhiteSpace(clone)) throw new GitPlatformException("GitHub 未回傳 Clone URL。");
            return new TargetRepository
            {
                PushUrl = GitHelper.InjectAuth(clone, "x-access-token", credential.Token),
                WebUrl = Rest.Str(json.RootElement, "html_url"),
                WasCreated = wasCreated
            };
        }
    }

    private static RepositoryInfo Map(JsonElement item)
    {
        var hasOwner = item.TryGetProperty("owner", out var ownerObject);
        var owner = hasOwner ? Rest.Str(ownerObject, "login") : string.Empty;
        var ownerType = hasOwner ? Rest.Str(ownerObject, "type") : string.Empty;
        var sizeKb = Rest.Long(item, "size");
        var history = sizeKb.HasValue ? checked(sizeKb.Value * 1024) : (long?)null;
        return new RepositoryInfo
        {
            Name = Rest.Str(item, "name"),
            Owner = owner,
            IsOrganizationOwned = RepositoryInfo.OwnerTypeIsOrganization(ownerType),
            NativeId = Rest.Long(item, "id")?.ToString() ?? string.Empty,
            CloneUrl = Rest.Str(item, "clone_url"),
            WebUrl = Rest.Str(item, "html_url"),
            Description = Rest.Str(item, "description"),
            DefaultBranch = Rest.Str(item, "default_branch"),
            HistorySizeInBytes = history,
            SizeInBytes = history,
            Stars = Rest.Int(item, "stargazers_count"),
            Forks = Rest.Int(item, "forks_count"),
            IsPrivate = Rest.Bool(item, "private"),
            LastUpdated = Rest.Date(item, "updated_at"),
            Language = Rest.Str(item, "language"),
            Topics = Rest.StringList(item, "topics")
        };
    }

    private static string ApiBase(AccountCredential credential)
    {
        if (string.IsNullOrWhiteSpace(credential.ApiUrl)) return "https://api.github.com";
        var root = credential.ApiUrl.TrimEnd('/');
        if (root.Equals("https://github.com", StringComparison.OrdinalIgnoreCase)) return "https://api.github.com";
        return root.EndsWith("/api/v3", StringComparison.OrdinalIgnoreCase) ? root : $"{root}/api/v3";
    }

    private static string Description(RepositoryInfo source) =>
        string.IsNullOrWhiteSpace(source.Description) ? $"Mirror of {source.FullName}" : source.Description;

    private static JsonDocument Parse(string body, string operation)
    {
        try { return JsonDocument.Parse(body); }
        catch (JsonException ex) { throw new GitPlatformException($"{operation}：回應不是合法 JSON。", ex); }
    }
}

internal static class PlatformHelpers
{
    public static GitPlatformException RequestFailed(string operation, HttpStatusCode status, string body)
    {
        var detail = Rest.ExtractMessage(body);
        var message = status switch
        {
            HttpStatusCode.Unauthorized => "Token 無效或已過期",
            HttpStatusCode.Forbidden => "Token 權限不足",
            HttpStatusCode.NotFound => "找不到資源",
            HttpStatusCode.Conflict => "資源已存在",
            HttpStatusCode.UnprocessableEntity => "名稱或欄位不被平台接受",
            _ => $"HTTP {(int)status}"
        };
        return new GitPlatformException(string.IsNullOrWhiteSpace(detail)
            ? $"{operation}：{message}"
            : $"{operation}：{message} — {detail}");
    }

    public static JsonDocument Parse(string body, string operation)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body); }
        catch (JsonException ex) { throw new GitPlatformException($"{operation}：回應不是合法 JSON。", ex); }
    }
}
