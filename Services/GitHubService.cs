using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GithubMirror.Models;

namespace GithubMirror.Services;

public sealed class GitHubService : IGitService
{
    public string PlatformName => "GitHub";

    public async Task<IReadOnlyList<RepositoryInfo>> GetRepositoriesAsync(AccountCredential credential, CancellationToken cancellationToken = default)
    {
        Validate(credential);
        var repositories = new List<RepositoryInfo>();
        var api = ApiBase(credential);

        for (var page = 1; ; page++)
        {
            using var document = await HttpApi.SendBearerAsync(HttpMethod.Get,
                $"{api}/user/repos?per_page=100&page={page}&sort=updated&affiliation=owner,collaborator,organization_member",
                credential.Token, cancellationToken: cancellationToken);
            var items = document.RootElement;
            foreach (var item in items.EnumerateArray()) repositories.Add(Map(item, credential));
            if (items.GetArrayLength() < 100) break;
        }

        return repositories;
    }

    public Task<GitRemote> GetSourceRemoteAsync(RepositoryInfo repository, AccountCredential credential, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GitRemote(repository.CloneUrl, "x-access-token", credential.Token));

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
            document = await HttpApi.SendBearerAsync(HttpMethod.Post, createUrl, credential.Token, new
            {
                name = targetRepositoryName,
                description = Description(sourceRepository),
                @private = sourceRepository.IsPrivate,
                has_issues = true,
                has_wiki = true
            }, cancellationToken);
        }
        catch (ApiRequestException ex) when (ex.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
        {
            document = await HttpApi.SendBearerAsync(HttpMethod.Get,
                $"{api}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(targetRepositoryName)}",
                credential.Token, cancellationToken: cancellationToken);
        }

        using (document)
        {
            var url = document.RootElement.String("clone_url");
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("GitHub 未回傳 Clone URL。");
            return new GitRemote(url, "x-access-token", credential.Token);
        }
    }

    private static RepositoryInfo Map(JsonElement item, AccountCredential credential)
    {
        var owner = item.TryGetProperty("owner", out var ownerObject) ? ownerObject.String("login") : credential.Username;
        return new RepositoryInfo
        {
            Name = item.String("name"),
            Owner = owner,
            CloneUrl = item.String("clone_url"),
            WebUrl = item.String("html_url"),
            Description = item.String("description"),
            SizeInBytes = checked(item.Int64("size") * 1024),
            Stars = item.Int32("stargazers_count"),
            Forks = item.Int32("forks_count"),
            IsPrivate = item.Boolean("private"),
            LastUpdated = item.Date("updated_at"),
            PrimaryLanguage = string.IsNullOrWhiteSpace(item.String("language")) ? "—" : item.String("language"),
            SourcePlatform = "GitHub",
            SourceAccountName = credential.DisplayName,
            SourceAccount = credential
        };
    }

    private static string ApiBase(AccountCredential credential) =>
        (string.IsNullOrWhiteSpace(credential.ApiUrl) ? "https://api.github.com" : credential.ApiUrl).TrimEnd('/');

    private static string Description(RepositoryInfo repository) =>
        string.IsNullOrWhiteSpace(repository.Description) ? $"Mirror of {repository.FullName}" : repository.Description;

    private static void Validate(AccountCredential credential)
    {
        AccountCredentialValidator.EnsureValid(credential);
    }
}
