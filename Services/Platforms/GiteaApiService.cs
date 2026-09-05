using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GithubMirror.Models;

namespace GithubMirror.Services.Platforms;

public class GiteaApiService : IGitService
{
    public virtual string Platform => PlatformIds.Gitea;
    protected virtual string DefaultRoot => string.Empty;

    public async Task<ProbeResult> ProbeAsync(AccountCredential credential, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(credential.Token)) return ProbeResult.Fail($"請輸入 {Platform} Token。");
        try
        {
            using var request = Rest.Get($"{ApiBase(credential)}/user");
            Authorize(request, credential.Token);
            using var json = await Rest.SendJsonAsync(request, $"驗證 {Platform} Token", ct).ConfigureAwait(false);
            var login = Rest.Str(json.RootElement, "login");
            return string.IsNullOrWhiteSpace(login)
                ? ProbeResult.Fail($"{Platform} 未回傳使用者名稱。")
                : ProbeResult.Ok(login, Rest.Str(json.RootElement, "full_name", login));
        }
        catch (GitPlatformException ex) { return ProbeResult.Fail(ex.Message); }
    }

    public async Task<IReadOnlyList<RepositoryInfo>> ListRepositoriesAsync(
        AccountCredential credential, IProgress<string>? progress, CancellationToken ct)
    {
        var result = new List<RepositoryInfo>();
        for (var page = 1; ; page++)
        {
            progress?.Report($"正在讀取 {Platform} 專案（第 {page} 頁）…");
            using var request = Rest.Get($"{ApiBase(credential)}/user/repos?limit=100&page={page}");
            Authorize(request, credential.Token);
            using var json = await Rest.SendJsonAsync(request, $"讀取 {Platform} 專案", ct).ConfigureAwait(false);
            var items = json.RootElement;
            foreach (var item in items.EnumerateArray()) result.Add(Map(item));
            if (items.GetArrayLength() < 100) break;
        }
        await GitTreeSize.FillLatestCommitSizesAsync(
            result,
            (repo, token) => FetchLatestCommitSizeAsync(credential, repo, token),
            progress,
            "正在讀取最近一次提交大小",
            ct).ConfigureAwait(false);
        progress?.Report($"{Platform} 讀取完成，共 {result.Count} 個專案。");
        return result;
    }

    private async Task<long?> FetchLatestCommitSizeAsync(
        AccountCredential credential, RepositoryInfo repo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(repo.Owner) || string.IsNullOrWhiteSpace(repo.Name)) return null;
        var branch = string.IsNullOrWhiteSpace(repo.DefaultBranch) ? "HEAD" : repo.DefaultBranch;
        var url =
            $"{ApiBase(credential)}/repos/{Uri.EscapeDataString(repo.Owner)}/{Uri.EscapeDataString(repo.Name)}/git/trees/{Uri.EscapeDataString(branch)}?recursive=true";
        return await GitTreeSize.FetchRecursiveTreeSizeAsync(url, request => Authorize(request, credential.Token), ct)
            .ConfigureAwait(false);
    }

    public Task<string> BuildSourceUrlAsync(RepositoryInfo repo, AccountCredential credential, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(GitHelper.InjectAuth(repo.CloneUrl, credential.Username, credential.Token));
    }

    public async Task<TargetRepository> EnsureTargetAsync(
        AccountCredential credential, RepositoryInfo source, string targetName, bool isPrivate, CancellationToken ct)
    {
        targetName = GitHelper.SanitizeRepoName(targetName);
        var api = ApiBase(credential);
        var owner = string.IsNullOrWhiteSpace(credential.Organization) ? credential.Username : credential.Organization;
        var createUrl = string.IsNullOrWhiteSpace(credential.Organization)
            ? $"{api}/user/repos"
            : $"{api}/orgs/{Uri.EscapeDataString(credential.Organization)}/repos";
        using var create = Rest.PostJson(createUrl, new
        {
            name = targetName,
            description = string.IsNullOrWhiteSpace(source.Description) ? $"Mirror of {source.FullName}" : source.Description,
            @private = isPrivate,
            auto_init = false
        });
        Authorize(create, credential.Token);
        var response = await Rest.TrySendAsync(create, ct).ConfigureAwait(false);
        var wasCreated = response.Ok;
        JsonDocument json;
        if (response.Ok) json = PlatformHelpers.Parse(response.Body, $"建立 {Platform} repository");
        else if (response.Status is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            using var existing = Rest.Get($"{api}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(targetName)}");
            Authorize(existing, credential.Token);
            json = await Rest.SendJsonAsync(existing, $"取得既有 {Platform} repository", ct).ConfigureAwait(false);
        }
        else throw PlatformHelpers.RequestFailed($"建立 {Platform} repository", response.Status, response.Body);

        using (json)
        {
            var clone = Rest.Str(json.RootElement, "clone_url");
            if (string.IsNullOrWhiteSpace(clone)) throw new GitPlatformException($"{Platform} 未回傳 Clone URL。");
            return new TargetRepository
            {
                PushUrl = GitHelper.InjectAuth(clone, credential.Username, credential.Token),
                WebUrl = Rest.Str(json.RootElement, "html_url"),
                WasCreated = wasCreated
            };
        }
    }

    protected string ApiBase(AccountCredential credential)
    {
        var root = (string.IsNullOrWhiteSpace(credential.ApiUrl) ? DefaultRoot : credential.ApiUrl).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(root)) throw new GitPlatformException("請輸入 Gitea 伺服器網址。");
        return root.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase) ? root : $"{root}/api/v1";
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
            Stars = Rest.Int(item, "stars_count"),
            Forks = Rest.Int(item, "forks_count"),
            IsPrivate = Rest.Bool(item, "private"),
            LastUpdated = Rest.Date(item, "updated_at"),
            Language = Rest.Str(item, "language")
        };
    }

    private static void Authorize(HttpRequestMessage request, string token) =>
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", token);
}

public sealed class CodebergApiService : GiteaApiService
{
    public override string Platform => PlatformIds.Codeberg;
    protected override string DefaultRoot => "https://codeberg.org";
}
