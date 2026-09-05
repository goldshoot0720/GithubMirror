using GithubMirror.Models;
using GithubMirror.Services;
using GithubMirror.ViewModels;
using Xunit;

namespace GithubMirror.Tests;

public sealed class RepositoryScopeTests
{
    private static AccountCredential GitHubUser(string username) => new()
    {
        Platform = PlatformIds.GitHub,
        Username = username,
        Token = "token"
    };

    [Fact]
    public void GitHub_account_defaults_to_owned_repos_only()
    {
        var account = GitHubUser("goldshoot0720");
        var owned = new RepositoryInfo { Name = "fengbroaiappwrite", Owner = "goldshoot0720" };
        var collab = new RepositoryInfo { Name = "shared-notes", Owner = "octocat" };
        var org = new RepositoryInfo { Name = "company-website", Owner = "acme-org", IsOrganizationOwned = true };

        Assert.Equal(RepositoryAccessKind.Owned, RepositoryInfo.ClassifyAccess(owned, account));
        Assert.Equal(RepositoryAccessKind.Collaborator, RepositoryInfo.ClassifyAccess(collab, account));
        Assert.Equal(RepositoryAccessKind.Organization, RepositoryInfo.ClassifyAccess(org, account));

        Assert.True(RepositoryInfo.MatchesScope(RepositoryAccessKind.Owned, false, false));
        Assert.False(RepositoryInfo.MatchesScope(RepositoryAccessKind.Collaborator, false, false));
        Assert.False(RepositoryInfo.MatchesScope(RepositoryAccessKind.Organization, false, false));

        Assert.True(RepositoryInfo.MatchesScope(RepositoryAccessKind.Collaborator, true, false));
        Assert.False(RepositoryInfo.MatchesScope(RepositoryAccessKind.Organization, true, false));

        Assert.True(RepositoryInfo.MatchesScope(RepositoryAccessKind.Collaborator, false, true));
        Assert.True(RepositoryInfo.MatchesScope(RepositoryAccessKind.Organization, false, true));
    }

    [Fact]
    public void Azure_and_Bitbucket_listings_count_as_owned()
    {
        var azure = new AccountCredential { Platform = PlatformIds.AzureRepos, Username = "pat", Organization = "org" };
        var bitbucket = new AccountCredential { Platform = PlatformIds.Bitbucket, Username = "alice", Organization = "team-ws" };
        var repo = new RepositoryInfo { Name = "app", Owner = "team-ws" };

        Assert.Equal(RepositoryAccessKind.Owned, RepositoryInfo.ClassifyAccess(repo, azure));
        Assert.Equal(RepositoryAccessKind.Owned, RepositoryInfo.ClassifyAccess(repo, bitbucket));
    }

    [Fact]
    public async Task Sample_list_hides_collab_and_org_until_toggled()
    {
        var vm = new MainWindowViewModel(AppServices.CreateSample());
        vm.Accounts.Add(GitHubUser("goldshoot0720"));
        await vm.RefreshAsync();

        Assert.DoesNotContain(vm.Repositories, r => r.Name is "shared-notes" or "company-website");
        Assert.All(vm.Repositories, r => Assert.Equal("goldshoot0720", r.Owner));

        vm.IncludeCollaboratorRepos = true;
        Assert.Contains(vm.Repositories, r => r.Name == "shared-notes" && r.Owner == "octocat");
        Assert.DoesNotContain(vm.Repositories, r => r.Name == "company-website");

        vm.IncludeAllAccessibleRepos = true;
        Assert.Contains(vm.Repositories, r => r.Name == "shared-notes");
        Assert.Contains(vm.Repositories, r => r.Name == "company-website" && r.Owner == "acme-org");
    }

    [Fact]
    public async Task Size_column_defaults_to_latest_commit_and_can_show_full_history()
    {
        var vm = new MainWindowViewModel(AppServices.CreateSample());
        vm.Accounts.Add(GitHubUser("goldshoot0720"));
        await vm.RefreshAsync();

        var repo = Assert.Single(vm.Repositories, r => r.Name == "web-portal");
        Assert.True(repo.LatestCommitSizeInBytes < repo.HistorySizeInBytes);
        Assert.Equal(repo.LatestCommitSizeInBytes, repo.SizeInBytes);
        Assert.Equal(RepositoryInfo.FormatSize(repo.LatestCommitSizeInBytes), repo.SizeDisplay);

        vm.ShowAllCommitSizes = true;
        Assert.Equal(repo.HistorySizeInBytes, repo.SizeInBytes);
        Assert.Equal(RepositoryInfo.FormatSize(repo.HistorySizeInBytes), repo.SizeDisplay);

        vm.ShowAllCommitSizes = false;
        Assert.Equal(repo.LatestCommitSizeInBytes, repo.SizeInBytes);
    }

    [Fact]
    public async Task Open_repository_command_needs_a_repository_with_a_web_url()
    {
        var vm = new MainWindowViewModel(AppServices.CreateSample());
        vm.Accounts.Add(GitHubUser("goldshoot0720"));
        await vm.RefreshAsync();

        var repo = Assert.Single(vm.Repositories, r => r.Name == "web-portal");
        Assert.True(repo.HasWebUrl);
        Assert.Contains(repo.WebUrl, repo.WebUrlTip, StringComparison.Ordinal);
        Assert.True(vm.OpenRepositoryCommand.CanExecute(repo));

        Assert.False(vm.OpenRepositoryCommand.CanExecute(null));
        Assert.False(vm.OpenRepositoryCommand.CanExecute("web-portal"));

        var noUrl = new RepositoryInfo { Name = "no-url", Owner = "goldshoot0720" };
        Assert.False(noUrl.HasWebUrl);
        Assert.False(vm.OpenRepositoryCommand.CanExecute(noUrl));

        // 走 Execute（雙擊列的路徑不看 CanExecute）時要留下錯誤訊息，而不是靜靜什麼都不做。
        vm.OpenRepositoryCommand.Execute(noUrl);
        Assert.True(vm.StatusIsError);
        Assert.Contains("no-url", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchesSearch_looks_at_name_owner_language_and_platform()
    {
        var repo = new RepositoryInfo
        {
            Name = "web-portal",
            Owner = "goldshoot0720",
            Description = "公司入口網站",
            Language = "TypeScript",
            SourcePlatform = PlatformIds.GitHub,
            Topics = ["frontend", "dashboard"]
        };

        Assert.True(repo.MatchesSearch("portal"));
        Assert.True(repo.MatchesSearch("GOLDSHOOT"));
        Assert.True(repo.MatchesSearch("入口"));
        Assert.True(repo.MatchesSearch("type"));
        Assert.True(repo.MatchesSearch("github"));
        Assert.True(repo.MatchesSearch("dashboard"));
        Assert.False(repo.MatchesSearch("gitlab"));
        Assert.True(repo.MatchesSearch("  "));
        Assert.True(repo.MatchesSearch(null));
    }

    [Fact]
    public async Task Search_text_filters_the_visible_project_list()
    {
        var vm = new MainWindowViewModel(AppServices.CreateSample());
        vm.Accounts.Add(GitHubUser("goldshoot0720"));
        await vm.RefreshAsync();

        var before = vm.Repositories.Count;
        Assert.True(before > 1);

        vm.SearchText = "web-portal";
        Assert.Equal("web-portal", Assert.Single(vm.Repositories).Name);
        Assert.True(vm.HasSearchText);
        Assert.Contains("web-portal", vm.EmptyRepositoriesTitle, StringComparison.Ordinal);

        vm.SearchText = "definitely-not-a-repo-name";
        Assert.Empty(vm.Repositories);
        Assert.True(vm.HasNoRepositories);
        Assert.Contains("definitely-not-a-repo-name", vm.EmptyRepositoriesTitle, StringComparison.Ordinal);

        vm.ClearSearchCommand.Execute(null);
        Assert.Equal(string.Empty, vm.SearchText);
        Assert.Equal(before, vm.Repositories.Count);
        Assert.False(vm.HasSearchText);
    }
}
