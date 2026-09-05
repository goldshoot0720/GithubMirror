using GithubMirror.Models;
using GithubMirror.ViewModels;
using Xunit;

namespace GithubMirror.Tests;

public sealed class GitHubSetupTests
{
    [Fact]
    public void Wizard_requires_final_step_and_token_before_verification()
    {
        var vm = new MainWindowViewModel();
        Assert.True(vm.IsGitHubSetup);
        Assert.False(vm.ShowTokenEntry);
        Assert.False(vm.GitHubBackCommand.CanExecute(null));
        vm.NewToken = "ghp_example";
        Assert.False(vm.CanAddAccount);
        for (var i = 0; i < 3; i++) vm.GitHubNextCommand.Execute(null);
        Assert.True(vm.ShowTokenEntry);
        Assert.True(vm.CanAddAccount);
        Assert.False(vm.GitHubNextCommand.CanExecute(null));
        vm.NewToken = " ";
        Assert.False(vm.CanAddAccount);
        vm.GitHubBackCommand.Execute(null);
        Assert.False(vm.ShowTokenEntry);
    }

    [Fact]
    public void Existing_token_shortcut_and_other_platforms_remain_available()
    {
        var vm = new MainWindowViewModel();
        vm.GitHubPasteCommand.Execute(null);
        Assert.Equal(4, vm.GitHubSetupStep);
        vm.GitHubSetupStep = 1;
        vm.NewPlatform = PlatformIds.GitLab;
        vm.IsSetupGuideOpen = true;
        Assert.False(vm.IsGitHubSetup);
        Assert.True(vm.ShowTokenEntry);
        Assert.True(vm.ShowGenericSetupGuide);
        vm.NewToken = "glpat_example";
        Assert.True(vm.CanAddAccount);
        vm.NewPlatform = PlatformIds.GitHub;
        Assert.False(vm.ShowGenericSetupGuide);
        Assert.False(vm.CanAddAccount);
    }
}
