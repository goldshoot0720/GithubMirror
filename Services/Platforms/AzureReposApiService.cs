using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GithubMirror.Models;

namespace GithubMirror.Services.Platforms;

public sealed class AzureReposApiService : IGitService
{
    public string Platform => PlatformIds.AzureRepos;

    public async Task<ProbeResult> ProbeAsync(AccountCredential credential, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(credential.Token)) return ProbeResult.Fail("請輸入 Azure DevOps PAT。");
        if (string.IsNullOrWhiteSpace(credential.Organization)) return ProbeResult.Fail("請輸入 Azure DevOps Organization。");
        try
        {
            using var request = Rest.Get($"{Root(credential)}/{Esc(credential.Organization)}/_apis/projects?$top=1&api-version=7.1");
            Authorize(request, credential);
            using var _ = await Rest.SendJsonAsync(request, "驗證 Azure DevOps PAT", ct).ConfigureAwait(false);
            var name = string.IsNullOrWhiteSpace(credential.Username) ? credential.Organization : credential.Username;
            return ProbeResult.Ok(name, credential.Organization);
        }
        catch (GitPlatformException ex) { return ProbeResult.Fail(ex.Message); }
    }

    public async Task<IReadOnlyList<RepositoryInfo>> ListRepositoriesAsync(
        AccountCredential credential, IProgress<string>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(credential.Organization)) throw new GitPlatformException("請輸入 Azure DevOps Organization。");
        progress?.Report("正在讀取 Azure Repos 專案…");
        var projectPart = string.IsNullOrWhiteSpace(credential.Project) ? string.Empty : $"/{Esc(credential.Project)}";
        using var request = Rest.Get($"{Root(credential)}/{Esc(credential.Organization)}{projectPart}/_apis/git/repositories?includeAllUrls=true&api-version=7.1");
        Authorize(request, credential);
        using var json = await Rest.SendJsonAsync(request, "讀取 Azure Repos 專案", ct).ConfigureAwait(false);
        var result = new List<RepositoryInfo>();
        if (json.RootElement.TryGetProperty("value", out var values))
            foreach (var item in values.EnumerateArray()) result.Add(Map(item));
        progress?.Report($"Azure Repos 讀取完成，共 {result.Count} 個專案。");
        return result;
    }

    public Task<string> BuildSourceUrlAsync(RepositoryInfo repo, AccountCredential credential, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(GitHelper.InjectAuth(repo.CloneUrl,
            string.IsNullOrWhiteSpace(credential.Username) ? "pat" : credential.Username, credential.Token));
    }

    public async Task<TargetRepository> EnsureTargetAsync(
        AccountCredential credential, RepositoryInfo source, string targetName, bool isPrivate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(credential.Organization)) throw new GitPlatformException("Azure DevOps Organization 不可空白。");
        var project = credential.Project;
        if (string.IsNullOrWhiteSpace(project) && source.SourcePlatform == PlatformIds.AzureRepos)
            project = source.Owner;
        if (string.IsNullOrWhiteSpace(project))
            throw new GitPlatformException("建立 Azure Repos 目標時必須在帳號進階設定指定 Project。");

        targetName = GitHelper.SanitizeRepoName(targetName);
        var collection = $"{Root(credential)}/{Esc(credential.Organization)}/{Esc(project)}/_apis/git/repositories";
        using var create = Rest.PostJson($"{collection}?api-version=7.1", new { name = targetName });
        Authorize(create, credential);
        var response = await Rest.TrySendAsync(create, ct).ConfigureAwait(false);
        var wasCreated = response.Ok;
        JsonDocument json;
        if (response.Ok) json = PlatformHelpers.Parse(response.Body, "建立 Azure Repos repository");
        else if (response.Status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            using var existing = Rest.Get($"{collection}/{Esc(targetName)}?api-version=7.1");
            Authorize(existing, credential);
            json = await Rest.SendJsonAsync(existing, "取得既有 Azure Repos repository", ct).ConfigureAwait(false);
        }
        else throw PlatformHelpers.RequestFailed("建立 Azure Repos repository", response.Status, response.Body);

        using (json)
        {
            var root = json.RootElement;
            var remote = Rest.Str(root, "remoteUrl");
            if (string.IsNullOrWhiteSpace(remote)) throw new GitPlatformException("Azure Repos 未回傳 Remote URL。");
            return new TargetRepository
            {
                PushUrl = GitHelper.InjectAuth(remote, string.IsNullOrWhiteSpace(credential.Username) ? "pat" : credential.Username, credential.Token),
                WebUrl = Rest.Str(root, "webUrl", remote),
                WasCreated = wasCreated
            };
        }
    }

    private static RepositoryInfo Map(JsonElement item)
    {
        var project = item.TryGetProperty("project", out var projectObject) ? Rest.Str(projectObject, "name") : string.Empty;
        return new RepositoryInfo
        {
            Name = Rest.Str(item, "name"),
            Owner = project,
            NativeId = Rest.Str(item, "id"),
            CloneUrl = Rest.Str(item, "remoteUrl"),
            WebUrl = Rest.Str(item, "webUrl", Rest.Str(item, "remoteUrl")),
            DefaultBranch = Rest.Str(item, "defaultBranch").Replace("refs/heads/", string.Empty, StringComparison.Ordinal),
            SizeInBytes = Rest.Long(item, "size"),
            HistorySizeInBytes = Rest.Long(item, "size"),
            LatestCommitSizeInBytes = Rest.Long(item, "size"),
            IsPrivate = true
        };
    }

    private static string Root(AccountCredential credential) =>
        (string.IsNullOrWhiteSpace(credential.ApiUrl) ? "https://dev.azure.com" : credential.ApiUrl).TrimEnd('/');

    private static string Esc(string value) => Uri.EscapeDataString(value);

    private static void Authorize(HttpRequestMessage request, AccountCredential credential) =>
        Rest.UseBasic(request, string.IsNullOrWhiteSpace(credential.Username) ? "pat" : credential.Username, credential.Token);
}
