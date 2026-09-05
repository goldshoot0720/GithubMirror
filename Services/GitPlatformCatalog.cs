using System;
using System.Collections.Generic;
using System.Linq;
using GithubMirror.Models;

namespace GithubMirror.Services;

/// <summary>
/// UI-independent platform metadata. The Avalonia layer can use this catalog to decide
/// which account fields to display without duplicating provider rules.
/// </summary>
public static class GitPlatformCatalog
{
    public static IReadOnlyList<GitPlatformDescriptor> All { get; } =
    [
        new("GitHub", "https://api.github.com", true, true, false, false, false,
            "Username", "Personal Access Token", "Target organization (optional)"),
        new("GitLab", "https://gitlab.com", true, true, false, false, false,
            "Username", "Personal Access Token", "Namespace / group (optional)"),
        new("Bitbucket", "https://api.bitbucket.org/2.0", true, true, false, false, false,
            "Atlassian email / username", "Bitbucket API Token", "Workspace (optional)"),
        new("Codeberg", "https://codeberg.org", true, true, false, false, false,
            "Username", "Access Token", "Target organization (optional)"),
        new("Gitea", string.Empty, true, true, true, false, false,
            "Username", "Access Token", "Target organization (optional)"),
        new("AWS CodeCommit", string.Empty, false, false, false, false, true,
            "HTTPS Git username (optional)", "HTTPS Git password (optional)", string.Empty),
        new("Azure Repos", "https://dev.azure.com", false, true, false, true, false,
            "Username (optional)", "Azure DevOps PAT", "Azure DevOps organization")
    ];

    public static GitPlatformDescriptor Get(string platform) =>
        All.FirstOrDefault(x => string.Equals(x.Name, platform, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"不支援的平台：{platform}", nameof(platform));
}

public sealed record GitPlatformDescriptor(
    string Name,
    string DefaultApiUrl,
    bool SupportsRepositoryListing,
    bool RequiresToken,
    bool RequiresApiUrl,
    bool RequiresProject,
    bool RequiresRegion,
    string UsernameLabel,
    string TokenLabel,
    string OrganizationLabel);

public static class AccountCredentialValidator
{
    public static IReadOnlyList<string> Validate(AccountCredential credential)
    {
        var errors = new List<string>();
        GitPlatformDescriptor descriptor;
        try
        {
            descriptor = GitPlatformCatalog.Get(credential.Platform);
        }
        catch (ArgumentException ex)
        {
            return [ex.Message];
        }

        if (descriptor.RequiresToken && string.IsNullOrWhiteSpace(credential.Token))
            errors.Add($"{descriptor.TokenLabel} 不可空白。");
        if (descriptor.Name is not ("AWS CodeCommit" or "Azure Repos") && string.IsNullOrWhiteSpace(credential.Username))
            errors.Add($"{descriptor.UsernameLabel} 不可空白。");
        if (descriptor.RequiresApiUrl && string.IsNullOrWhiteSpace(credential.ApiUrl))
            errors.Add("伺服器網址不可空白。");
        if (!string.IsNullOrWhiteSpace(credential.ApiUrl) &&
            (!Uri.TryCreate(credential.ApiUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")))
            errors.Add("伺服器網址必須是有效的 HTTP 或 HTTPS URL。");
        if (credential.Platform == "Azure Repos" && string.IsNullOrWhiteSpace(credential.Organization))
            errors.Add("Azure DevOps 組織不可空白。");
        if (descriptor.RequiresProject && string.IsNullOrWhiteSpace(credential.Project))
            errors.Add("Azure DevOps Project 不可空白。");
        if (descriptor.RequiresRegion && string.IsNullOrWhiteSpace(credential.Region))
            errors.Add("AWS Region 不可空白。");
        if (credential.Platform == "AWS CodeCommit" &&
            string.IsNullOrWhiteSpace(credential.Username) != string.IsNullOrWhiteSpace(credential.Token))
            errors.Add("CodeCommit HTTPS Git 使用者名稱與密碼必須同時填寫或同時留白。");

        return errors;
    }

    public static void EnsureValid(AccountCredential credential)
    {
        var errors = Validate(credential);
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
    }
}
