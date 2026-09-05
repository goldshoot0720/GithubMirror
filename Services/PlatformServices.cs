using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GithubMirror.Models;

namespace GithubMirror.Services;

public sealed class GitLabService : IGitService
{
    public string PlatformName => "GitLab";

    public async Task<IReadOnlyList<RepositoryInfo>> GetRepositoriesAsync(AccountCredential credential, CancellationToken cancellationToken = default)
    {
        AccountCredentialValidator.EnsureValid(credential);
        var result = new List<RepositoryInfo>();
        for (var page = 1; ; page++)
        {
            using var document = await HttpApi.SendPrivateTokenAsync(HttpMethod.Get,
                $"{ApiBase(credential)}/projects?membership=true&statistics=true&per_page=100&page={page}&order_by=updated_at&sort=desc",
                credential.Token, cancellationToken: cancellationToken);
            var items = document.RootElement;
            foreach (var item in items.EnumerateArray())
            {
                var path = item.String("path_with_namespace");
                var split = path.LastIndexOf('/');
                var size = item.TryGetProperty("statistics", out var statistics) ? statistics.Int64("repository_size") : 0;
                result.Add(new RepositoryInfo
                {
                    Name = item.String("name"),
                    Owner = split > 0 ? path[..split] : credential.Username,
                    CloneUrl = item.String("http_url_to_repo"),
                    WebUrl = item.String("web_url"),
                    Description = item.String("description"),
                    SizeInBytes = size,
                    Stars = item.Int32("star_count"),
                    Forks = item.Int32("forks_count"),
                    IsPrivate = item.String("visibility") != "public",
                    LastUpdated = item.Date("last_activity_at"),
                    SourcePlatform = PlatformName,
                    SourceAccountName = credential.DisplayName,
                    SourceAccount = credential
                });
            }
            if (items.GetArrayLength() < 100) break;
        }
        return result;
    }

    public Task<GitRemote> GetSourceRemoteAsync(RepositoryInfo repository, AccountCredential credential, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GitRemote(repository.CloneUrl, "oauth2", credential.Token));

    public async Task<GitRemote> PrepareTargetRepositoryAsync(AccountCredential credential, RepositoryInfo sourceRepository, string targetRepositoryName, CancellationToken cancellationToken = default)
    {
        AccountCredentialValidator.EnsureValid(credential);
        var api = ApiBase(credential);
        long? namespaceId = null;
        if (!string.IsNullOrWhiteSpace(credential.Organization))
        {
            if (long.TryParse(credential.Organization, out var parsed)) namespaceId = parsed;
            else
            {
                using var namespaces = await HttpApi.SendPrivateTokenAsync(HttpMethod.Get,
                    $"{api}/namespaces?search={Uri.EscapeDataString(credential.Organization)}&per_page=100", credential.Token,
                    cancellationToken: cancellationToken);
                foreach (var item in namespaces.RootElement.EnumerateArray())
                {
                    if (string.Equals(item.String("full_path"), credential.Organization, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(item.String("path"), credential.Organization, StringComparison.OrdinalIgnoreCase))
                    {
                        namespaceId = item.Int64("id");
                        break;
                    }
                }
                if (namespaceId is null) throw new InvalidOperationException($"找不到 GitLab namespace：{credential.Organization}");
            }
        }

        JsonDocument document;
        try
        {
            document = await HttpApi.SendPrivateTokenAsync(HttpMethod.Post, $"{api}/projects", credential.Token, new
            {
                name = targetRepositoryName,
                path = targetRepositoryName,
                namespace_id = namespaceId,
                description = MirrorDescription(sourceRepository),
                visibility = sourceRepository.IsPrivate ? "private" : "public"
            }, cancellationToken);
        }
        catch (ApiRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            var owner = string.IsNullOrWhiteSpace(credential.Organization) ? credential.Username : credential.Organization;
            var projectPath = Uri.EscapeDataString($"{owner}/{targetRepositoryName}");
            document = await HttpApi.SendPrivateTokenAsync(HttpMethod.Get, $"{api}/projects/{projectPath}", credential.Token,
                cancellationToken: cancellationToken);
        }

        using (document)
        {
            var cloneUrl = document.RootElement.String("http_url_to_repo");
            if (string.IsNullOrWhiteSpace(cloneUrl)) throw new InvalidOperationException("GitLab 未回傳 Clone URL。");
            return new GitRemote(cloneUrl, "oauth2", credential.Token);
        }
    }

    private static string ApiBase(AccountCredential credential)
    {
        var root = (string.IsNullOrWhiteSpace(credential.ApiUrl) ? "https://gitlab.com" : credential.ApiUrl).TrimEnd('/');
        return root.EndsWith("/api/v4", StringComparison.OrdinalIgnoreCase) ? root : $"{root}/api/v4";
    }

    private static string MirrorDescription(RepositoryInfo repository) =>
        string.IsNullOrWhiteSpace(repository.Description) ? $"Mirror of {repository.FullName}" : repository.Description;

}

public sealed class BitbucketService : IGitService
{
    public string PlatformName => "Bitbucket";

    public async Task<IReadOnlyList<RepositoryInfo>> GetRepositoriesAsync(AccountCredential credential, CancellationToken cancellationToken = default)
    {
        Validate(credential);
        var result = new List<RepositoryInfo>();
        var workspace = Workspace(credential);
        var next = $"https://api.bitbucket.org/2.0/repositories/{Uri.EscapeDataString(workspace)}?pagelen=100&sort=-updated_on";
        while (!string.IsNullOrWhiteSpace(next))
        {
            using var document = await HttpApi.SendBasicAsync(HttpMethod.Get, next, credential.Username, credential.Token,
                cancellationToken: cancellationToken);
            var root = document.RootElement;
            foreach (var item in root.GetProperty("values").EnumerateArray())
            {
                result.Add(new RepositoryInfo
                {
                    Name = item.String("name"),
                    Owner = workspace,
                    CloneUrl = HttpsClone(item),
                    WebUrl = Link(item, "html"),
                    Description = item.String("description"),
                    SizeInBytes = item.Int64("size"),
                    IsPrivate = item.Boolean("is_private"),
                    LastUpdated = item.Date("updated_on"),
                    PrimaryLanguage = string.IsNullOrWhiteSpace(item.String("language")) ? "—" : item.String("language"),
                    SourcePlatform = PlatformName,
                    SourceAccountName = credential.DisplayName,
                    SourceAccount = credential
                });
            }
            next = root.String("next");
        }
        return result;
    }

    public Task<GitRemote> GetSourceRemoteAsync(RepositoryInfo repository, AccountCredential credential, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GitRemote(repository.CloneUrl, credential.Username, credential.Token));

    public async Task<GitRemote> PrepareTargetRepositoryAsync(AccountCredential credential, RepositoryInfo sourceRepository, string targetRepositoryName, CancellationToken cancellationToken = default)
    {
        Validate(credential);
        var workspace = Workspace(credential);
        var url = $"https://api.bitbucket.org/2.0/repositories/{Uri.EscapeDataString(workspace)}/{Uri.EscapeDataString(targetRepositoryName)}";
        JsonDocument document;
        try
        {
            document = await HttpApi.SendBasicAsync(HttpMethod.Post, url, credential.Username, credential.Token, new
            {
                scm = "git",
                name = targetRepositoryName,
                description = MirrorDescription(sourceRepository),
                is_private = sourceRepository.IsPrivate
            }, cancellationToken);
        }
        catch (ApiRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            document = await HttpApi.SendBasicAsync(HttpMethod.Get, url, credential.Username, credential.Token,
                cancellationToken: cancellationToken);
        }

        using (document)
        {
            var cloneUrl = HttpsClone(document.RootElement);
            if (string.IsNullOrWhiteSpace(cloneUrl)) throw new InvalidOperationException("Bitbucket 未回傳 HTTPS Clone URL。");
            return new GitRemote(cloneUrl, credential.Username, credential.Token);
        }
    }

    private static string Workspace(AccountCredential credential) =>
        string.IsNullOrWhiteSpace(credential.Organization) ? credential.Username : credential.Organization;

    private static string Link(JsonElement repository, string name)
    {
        if (!repository.TryGetProperty("links", out var links) || !links.TryGetProperty(name, out var link)) return string.Empty;
        return link.String("href");
    }

    private static string HttpsClone(JsonElement repository)
    {
        if (!repository.TryGetProperty("links", out var links) || !links.TryGetProperty("clone", out var clones)) return string.Empty;
        foreach (var clone in clones.EnumerateArray())
            if (clone.String("name") == "https") return clone.String("href");
        return string.Empty;
    }

    private static void Validate(AccountCredential credential)
    {
        AccountCredentialValidator.EnsureValid(credential);
    }

    private static string MirrorDescription(RepositoryInfo repository) =>
        string.IsNullOrWhiteSpace(repository.Description) ? $"Mirror of {repository.FullName}" : repository.Description;
}

public class GiteaService : IGitService
{
    public virtual string PlatformName => "Gitea";
    protected virtual string DefaultServer => string.Empty;

    public async Task<IReadOnlyList<RepositoryInfo>> GetRepositoriesAsync(AccountCredential credential, CancellationToken cancellationToken = default)
    {
        Validate(credential);
        var result = new List<RepositoryInfo>();
        for (var page = 1; ; page++)
        {
            using var document = await HttpApi.SendGiteaTokenAsync(HttpMethod.Get,
                $"{ApiBase(credential)}/user/repos?limit=100&page={page}", credential.Token,
                cancellationToken: cancellationToken);
            var items = document.RootElement;
            foreach (var item in items.EnumerateArray())
            {
                var owner = item.TryGetProperty("owner", out var ownerObject) ? ownerObject.String("login") : credential.Username;
                result.Add(new RepositoryInfo
                {
                    Name = item.String("name"),
                    Owner = owner,
                    CloneUrl = item.String("clone_url"),
                    WebUrl = item.String("html_url"),
                    Description = item.String("description"),
                    SizeInBytes = checked(item.Int64("size") * 1024),
                    Stars = item.Int32("stars_count"),
                    Forks = item.Int32("forks_count"),
                    IsPrivate = item.Boolean("private"),
                    LastUpdated = item.Date("updated_at"),
                    PrimaryLanguage = string.IsNullOrWhiteSpace(item.String("language")) ? "—" : item.String("language"),
                    SourcePlatform = PlatformName,
                    SourceAccountName = credential.DisplayName,
                    SourceAccount = credential
                });
            }
            if (items.GetArrayLength() < 100) break;
        }
        return result;
    }

    public Task<GitRemote> GetSourceRemoteAsync(RepositoryInfo repository, AccountCredential credential, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GitRemote(repository.CloneUrl, credential.Username, credential.Token));

    public async Task<GitRemote> PrepareTargetRepositoryAsync(AccountCredential credential, RepositoryInfo sourceRepository, string targetRepositoryName, CancellationToken cancellationToken = default)
    {
        Validate(credential);
        var api = ApiBase(credential);
        var owner = string.IsNullOrWhiteSpace(credential.Organization) ? credential.Username : credential.Organization;
        var createUrl = string.IsNullOrWhiteSpace(credential.Organization)
            ? $"{api}/user/repos"
            : $"{api}/orgs/{Uri.EscapeDataString(credential.Organization)}/repos";

        JsonDocument document;
        try
        {
            document = await HttpApi.SendGiteaTokenAsync(HttpMethod.Post, createUrl, credential.Token, new
            {
                name = targetRepositoryName,
                description = string.IsNullOrWhiteSpace(sourceRepository.Description) ? $"Mirror of {sourceRepository.FullName}" : sourceRepository.Description,
                @private = sourceRepository.IsPrivate,
                auto_init = false
            }, cancellationToken);
        }
        catch (ApiRequestException ex) when (ex.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
        {
            document = await HttpApi.SendGiteaTokenAsync(HttpMethod.Get,
                $"{api}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(targetRepositoryName)}", credential.Token,
                cancellationToken: cancellationToken);
        }

        using (document)
        {
            var cloneUrl = document.RootElement.String("clone_url");
            if (string.IsNullOrWhiteSpace(cloneUrl)) throw new InvalidOperationException($"{PlatformName} 未回傳 Clone URL。");
            return new GitRemote(cloneUrl, credential.Username, credential.Token);
        }
    }

    protected string ApiBase(AccountCredential credential)
    {
        var root = (string.IsNullOrWhiteSpace(credential.ApiUrl) ? DefaultServer : credential.ApiUrl).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Gitea 伺服器網址不可空白。");
        return root.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase) ? root : $"{root}/api/v1";
    }

    private void Validate(AccountCredential credential)
    {
        AccountCredentialValidator.EnsureValid(credential);
        _ = ApiBase(credential);
    }
}

public sealed class CodebergService : GiteaService
{
    public override string PlatformName => "Codeberg";
    protected override string DefaultServer => "https://codeberg.org";
}

public sealed class AzureReposService : IGitService
{
    public string PlatformName => "Azure Repos";

    public async Task<IReadOnlyList<RepositoryInfo>> GetRepositoriesAsync(AccountCredential credential, CancellationToken cancellationToken = default)
    {
        Validate(credential);
        using var document = await HttpApi.SendBasicAsync(HttpMethod.Get, RepositoriesUrl(credential),
            string.IsNullOrWhiteSpace(credential.Username) ? "pat" : credential.Username, credential.Token,
            cancellationToken: cancellationToken);
        var result = new List<RepositoryInfo>();
        foreach (var item in document.RootElement.GetProperty("value").EnumerateArray())
        {
            result.Add(new RepositoryInfo
            {
                Name = item.String("name"),
                Owner = credential.Project,
                CloneUrl = item.String("remoteUrl"),
                WebUrl = item.String("webUrl"),
                SizeInBytes = item.Int64("size"),
                IsPrivate = true,
                SourcePlatform = PlatformName,
                SourceAccountName = credential.DisplayName,
                SourceAccount = credential
            });
        }
        return result;
    }

    public Task<GitRemote> GetSourceRemoteAsync(RepositoryInfo repository, AccountCredential credential, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GitRemote(repository.CloneUrl, string.IsNullOrWhiteSpace(credential.Username) ? "pat" : credential.Username, credential.Token));

    public async Task<GitRemote> PrepareTargetRepositoryAsync(AccountCredential credential, RepositoryInfo sourceRepository, string targetRepositoryName, CancellationToken cancellationToken = default)
    {
        Validate(credential);
        var username = string.IsNullOrWhiteSpace(credential.Username) ? "pat" : credential.Username;
        JsonDocument document;
        try
        {
            document = await HttpApi.SendBasicAsync(HttpMethod.Post, RepositoriesUrl(credential), username, credential.Token,
                new { name = targetRepositoryName }, cancellationToken);
        }
        catch (ApiRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            document = await HttpApi.SendBasicAsync(HttpMethod.Get,
                $"{RepositoriesUrl(credential).Replace("?api-version=7.1", string.Empty)}/{Uri.EscapeDataString(targetRepositoryName)}?api-version=7.1",
                username, credential.Token, cancellationToken: cancellationToken);
        }
        using (document)
        {
            var cloneUrl = document.RootElement.String("remoteUrl");
            if (string.IsNullOrWhiteSpace(cloneUrl)) throw new InvalidOperationException("Azure Repos 未回傳 Remote URL。");
            return new GitRemote(cloneUrl, username, credential.Token);
        }
    }

    private static string RepositoriesUrl(AccountCredential credential)
    {
        var root = (string.IsNullOrWhiteSpace(credential.ApiUrl) ? "https://dev.azure.com" : credential.ApiUrl).TrimEnd('/');
        return $"{root}/{Uri.EscapeDataString(credential.Organization)}/{Uri.EscapeDataString(credential.Project)}/_apis/git/repositories?api-version=7.1";
    }

    private static void Validate(AccountCredential credential)
    {
        AccountCredentialValidator.EnsureValid(credential);
    }
}

public sealed class AwsCodeCommitService : IGitService
{
    public string PlatformName => "AWS CodeCommit";

    public async Task<IReadOnlyList<RepositoryInfo>> GetRepositoriesAsync(AccountCredential credential, CancellationToken cancellationToken = default)
    {
        Validate(credential);
        using var list = await RunAwsAsync(credential, ["codecommit", "list-repositories"], cancellationToken);
        var result = new List<RepositoryInfo>();
        if (!list.RootElement.TryGetProperty("repositories", out var repositories)) return result;
        foreach (var entry in repositories.EnumerateArray())
        {
            var name = entry.String("repositoryName");
            using var detail = await RunAwsAsync(credential, ["codecommit", "get-repository", "--repository-name", name], cancellationToken);
            var item = detail.RootElement.GetProperty("repositoryMetadata");
            var updated = item.TryGetProperty("lastModifiedDate", out var epoch) && epoch.TryGetDouble(out var seconds)
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000))
                : DateTimeOffset.MinValue;
            result.Add(new RepositoryInfo
            {
                Name = name,
                Owner = item.String("accountId"),
                CloneUrl = item.String("cloneUrlHttp"),
                Description = item.String("repositoryDescription"),
                LastUpdated = updated,
                IsPrivate = true,
                SourcePlatform = PlatformName,
                SourceAccountName = credential.DisplayName,
                SourceAccount = credential
            });
        }
        return result;
    }

    public Task<GitRemote> GetSourceRemoteAsync(RepositoryInfo repository, AccountCredential credential, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GitRemote(repository.CloneUrl, credential.Username, credential.Token));

    public async Task<GitRemote> PrepareTargetRepositoryAsync(AccountCredential credential, RepositoryInfo sourceRepository, string targetRepositoryName, CancellationToken cancellationToken = default)
    {
        Validate(credential);
        JsonDocument document;
        try
        {
            document = await RunAwsAsync(credential, ["codecommit", "get-repository", "--repository-name", targetRepositoryName], cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("RepositoryDoesNotExistException", StringComparison.OrdinalIgnoreCase))
        {
            var arguments = new List<string> { "codecommit", "create-repository", "--repository-name", targetRepositoryName };
            var description = string.IsNullOrWhiteSpace(sourceRepository.Description) ? $"Mirror of {sourceRepository.FullName}" : sourceRepository.Description;
            if (!string.IsNullOrWhiteSpace(description))
            {
                arguments.Add("--repository-description");
                arguments.Add(description.Length > 1000 ? description[..1000] : description);
            }
            document = await RunAwsAsync(credential, arguments, cancellationToken);
        }

        using (document)
        {
            var metadata = document.RootElement.GetProperty("repositoryMetadata");
            var url = metadata.String("cloneUrlHttp");
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("AWS CodeCommit 未回傳 HTTPS Clone URL。");
            return new GitRemote(url, credential.Username, credential.Token);
        }
    }

    private static async Task<JsonDocument> RunAwsAsync(AccountCredential credential, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "aws",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        psi.ArgumentList.Add("--region");
        psi.ArgumentList.Add(credential.Region);
        if (!string.IsNullOrWhiteSpace(credential.Profile))
        {
            psi.ArgumentList.Add("--profile");
            psi.ArgumentList.Add(credential.Profile);
        }
        psi.ArgumentList.Add("--output");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add("--no-cli-pager");

        try
        {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("無法啟動 AWS CLI。");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0) throw new InvalidOperationException($"AWS CLI 失敗：{error.Trim()}");
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(output) ? "{}" : output);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("找不到 AWS CLI。請先安裝 AWS CLI v2，並設定可存取 CodeCommit 的 Profile。", ex);
        }
    }

    private static void Validate(AccountCredential credential)
    {
        AccountCredentialValidator.EnsureValid(credential);
    }
}
