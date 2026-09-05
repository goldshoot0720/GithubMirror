using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GithubMirror.Models;

namespace GithubMirror.Services.Platforms;

public sealed class BitbucketApiService : IGitService
{
    private const string Api = "https://api.bitbucket.org/2.0";
    public string Platform => PlatformIds.Bitbucket;

    public async Task<ProbeResult> ProbeAsync(AccountCredential credential, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(credential.Token)) return ProbeResult.Fail("請輸入 Bitbucket App Password／Access Token。");
        if (string.IsNullOrWhiteSpace(credential.Username))
            return ProbeResult.Fail("Bitbucket App Password 驗證需要帳號名稱；請在進階欄位輸入 Username。若使用 OAuth Access Token，請將 Username 留白並改用 Bearer 流程。");
        try
        {
            using var request = Rest.Get($"{Api}/user");
            Authorize(request, credential);
            using var json = await Rest.SendJsonAsync(request, "驗證 Bitbucket 憑證", ct).ConfigureAwait(false);
            var root = json.RootElement;
            var username = Rest.Str(root, "username", Rest.Str(root, "nickname"));
            return string.IsNullOrWhiteSpace(username)
                ? ProbeResult.Fail("Bitbucket 未回傳使用者名稱。")
                : ProbeResult.Ok(username, Rest.Str(root, "display_name", username));
        }
        catch (GitPlatformException ex) { return ProbeResult.Fail(ex.Message); }
    }

    public async Task<IReadOnlyList<RepositoryInfo>> ListRepositoriesAsync(
        AccountCredential credential, IProgress<string>? progress, CancellationToken ct)
    {
        var workspace = Workspace(credential);
        if (string.IsNullOrWhiteSpace(workspace)) throw new GitPlatformException("請輸入 Bitbucket Workspace 或 Username。");
        var result = new List<RepositoryInfo>();
        var next = $"{Api}/repositories/{Uri.EscapeDataString(workspace)}?pagelen=100&sort=-updated_on";
        var page = 1;
        while (!string.IsNullOrWhiteSpace(next))
        {
            progress?.Report($"正在讀取 Bitbucket 專案（第 {page++} 頁）…");
            using var request = Rest.Get(next);
            Authorize(request, credential);
            using var json = await Rest.SendJsonAsync(request, "讀取 Bitbucket 專案", ct).ConfigureAwait(false);
            var root = json.RootElement;
            if (!root.TryGetProperty("values", out var values)) break;
            foreach (var item in values.EnumerateArray()) result.Add(Map(item, workspace));
            next = Rest.Str(root, "next");
        }
        progress?.Report($"Bitbucket 讀取完成，共 {result.Count} 個專案。");
        return result;
    }

    public Task<string> BuildSourceUrlAsync(RepositoryInfo repo, AccountCredential credential, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(GitHelper.InjectAuth(repo.CloneUrl, credential.Username, credential.Token));
    }

    public async Task<TargetRepository> EnsureTargetAsync(
        AccountCredential credential, RepositoryInfo source, string targetName, bool isPrivate, CancellationToken ct)
    {
        var workspace = Workspace(credential);
        if (string.IsNullOrWhiteSpace(workspace)) throw new GitPlatformException("請輸入 Bitbucket Workspace 或 Username。");
        targetName = GitHelper.SanitizeRepoName(targetName);
        var url = $"{Api}/repositories/{Uri.EscapeDataString(workspace)}/{Uri.EscapeDataString(targetName)}";
        using var create = Rest.PostJson(url, new
        {
            scm = "git",
            name = targetName,
            description = string.IsNullOrWhiteSpace(source.Description) ? $"Mirror of {source.FullName}" : source.Description,
            is_private = isPrivate
        });
        Authorize(create, credential);
        var response = await Rest.TrySendAsync(create, ct).ConfigureAwait(false);
        var wasCreated = response.Ok;
        JsonDocument json;
        if (response.Ok) json = PlatformHelpers.Parse(response.Body, "建立 Bitbucket repository");
        else if (response.Status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            using var existing = Rest.Get(url);
            Authorize(existing, credential);
            json = await Rest.SendJsonAsync(existing, "取得既有 Bitbucket repository", ct).ConfigureAwait(false);
        }
        else throw PlatformHelpers.RequestFailed("建立 Bitbucket repository", response.Status, response.Body);

        using (json)
        {
            var clone = HttpsClone(json.RootElement);
            if (string.IsNullOrWhiteSpace(clone)) throw new GitPlatformException("Bitbucket 未回傳 HTTPS Clone URL。");
            return new TargetRepository
            {
                PushUrl = GitHelper.InjectAuth(clone, credential.Username, credential.Token),
                WebUrl = Link(json.RootElement, "html"),
                WasCreated = wasCreated
            };
        }
    }

    private static RepositoryInfo Map(JsonElement item, string workspace) => new()
    {
        Name = Rest.Str(item, "name"),
        Owner = workspace,
        NativeId = Rest.Str(item, "uuid"),
        CloneUrl = HttpsClone(item),
        WebUrl = Link(item, "html"),
        Description = Rest.Str(item, "description"),
        DefaultBranch = item.TryGetProperty("mainbranch", out var branch) ? Rest.Str(branch, "name") : string.Empty,
        SizeInBytes = Rest.Long(item, "size"),
        HistorySizeInBytes = Rest.Long(item, "size"),
        LatestCommitSizeInBytes = Rest.Long(item, "size"),
        IsPrivate = Rest.Bool(item, "is_private"),
        LastUpdated = Rest.Date(item, "updated_on"),
        Language = Rest.Str(item, "language")
    };

    private static string Workspace(AccountCredential credential) =>
        string.IsNullOrWhiteSpace(credential.Organization) ? credential.Username : credential.Organization;

    private static string Link(JsonElement repository, string name)
    {
        if (!repository.TryGetProperty("links", out var links) || !links.TryGetProperty(name, out var value)) return string.Empty;
        return Rest.Str(value, "href");
    }

    private static string HttpsClone(JsonElement repository)
    {
        if (!repository.TryGetProperty("links", out var links) || !links.TryGetProperty("clone", out var values)) return string.Empty;
        foreach (var clone in values.EnumerateArray())
            if (Rest.Str(clone, "name") == "https") return Rest.Str(clone, "href");
        return string.Empty;
    }

    private static void Authorize(HttpRequestMessage request, AccountCredential credential) =>
        Rest.UseBasic(request, credential.Username, credential.Token);
}
