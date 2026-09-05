using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace GithubMirror.Models;

public class RepositoryInfo : ObservableObject
{
    private bool _isSelected;

    public string Name { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;

    /// <summary>平台原生 ID（GitLab 專案 ID、Azure repo GUID 等），建立目標時可能用得到。</summary>
    public string NativeId { get; set; } = string.Empty;

    public string CloneUrl { get; set; } = string.Empty;
    public string WebUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = string.Empty;

    /// <summary>清單上目前顯示的大小（bytes）。null 代表未知。</summary>
    private long? _sizeInBytes;
    public long? SizeInBytes
    {
        get => _sizeInBytes;
        set
        {
            if (SetProperty(ref _sizeInBytes, value))
                OnPropertyChanged(nameof(SizeDisplay));
        }
    }

    /// <summary>全部 Git 歷史（含已刪檔）的物件庫大小。</summary>
    public long? HistorySizeInBytes { get; set; }

    /// <summary>預設分支最近一次提交的工作樹大小（目前檔案加總）。</summary>
    public long? LatestCommitSizeInBytes { get; set; }

    public int Stars { get; set; }
    public int Forks { get; set; }
    public bool IsPrivate { get; set; }
    public DateTime? LastUpdated { get; set; }
    public string Language { get; set; } = string.Empty;
    public List<string> Topics { get; set; } = new();

    public string SourcePlatform { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string AccountDisplay { get; set; } = string.Empty;

    /// <summary>擁有者是組織／群組（相對於個人帳號）。</summary>
    public bool IsOrganizationOwned { get; set; }

    /// <summary>相對來源帳號：自己的、協作的、或組織的。</summary>
    public RepositoryAccessKind AccessKind { get; set; }

    /// <summary>清單勾選狀態，用於批次鏡像。</summary>
    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string FullName => string.IsNullOrEmpty(Owner) ? Name : $"{Owner}/{Name}";

    public string SizeDisplay => FormatSize(SizeInBytes);

    public string VisibilityDisplay => IsPrivate ? "Private" : "Public";

    public string LastUpdatedDisplay =>
        LastUpdated.HasValue ? LastUpdated.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";

    public string LanguageDisplay => string.IsNullOrWhiteSpace(Language) ? "—" : Language;

    public string DescriptionDisplay => string.IsNullOrWhiteSpace(Description) ? "—" : Description;

    public bool MatchesSearch(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        var needle = query.Trim();
        if (Contains(Name) || Contains(Owner) || Contains(FullName) ||
            Contains(Description) || Contains(Language) || Contains(SourcePlatform) ||
            Contains(AccountDisplay))
            return true;

        return Topics is { Count: > 0 } && Topics.Any(Contains);

        bool Contains(string? value) =>
            !string.IsNullOrEmpty(value) && value.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatSize(long? bytes)
    {
        if (!bytes.HasValue) return "—";
        if (bytes.Value <= 0) return "0 KB";

        double kb = bytes.Value / 1024.0;
        if (kb < 1024) return $"{kb:N0} KB";

        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:N1} MB";

        double gb = mb / 1024.0;
        return $"{gb:N2} GB";
    }

    public static long? DisplayedSize(long? latestCommit, long? allCommits, bool showAllCommitSizes) =>
        showAllCommitSizes ? allCommits : latestCommit ?? allCommits;

    public void ApplySizeMode(bool showAllCommitSizes) =>
        SizeInBytes = DisplayedSize(LatestCommitSizeInBytes, HistorySizeInBytes, showAllCommitSizes);

    public static bool OwnerTypeIsOrganization(string? type) =>
        type is "Organization" or "organization" or "Team" or "team" or "Group" or "group";

    public static bool IsAccountScopedListing(string platform) =>
        platform is PlatformIds.Bitbucket or PlatformIds.AwsCodeCommit or PlatformIds.AzureRepos;

    public static bool IsOwnedByAccount(RepositoryInfo repo, AccountCredential account)
    {
        if (IsAccountScopedListing(account.Platform)) return true;
        if (string.IsNullOrWhiteSpace(repo.Owner)) return true;
        return string.Equals(repo.Owner, account.Username, StringComparison.OrdinalIgnoreCase);
    }

    public static RepositoryAccessKind ClassifyAccess(RepositoryInfo repo, AccountCredential account)
    {
        if (IsOwnedByAccount(repo, account)) return RepositoryAccessKind.Owned;
        return repo.IsOrganizationOwned ? RepositoryAccessKind.Organization : RepositoryAccessKind.Collaborator;
    }

    public static bool MatchesScope(
        RepositoryAccessKind kind, bool includeCollaborators, bool includeAllAccessible)
    {
        if (includeAllAccessible) return true;
        if (kind == RepositoryAccessKind.Owned) return true;
        return includeCollaborators && kind == RepositoryAccessKind.Collaborator;
    }
}

public enum RepositoryAccessKind
{
    Owned,
    Collaborator,
    Organization
}
