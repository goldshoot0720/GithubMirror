using System.Diagnostics;
using System.Text;
using GithubMirror.Models;
using GithubMirror.Services;
using Xunit;

namespace GithubMirror.Tests;

public sealed class ServiceLayerTests
{
    [Fact]
    public void Provider_exposes_all_requested_platforms()
    {
        var provider = new RealGitServiceProvider();

        Assert.All(PlatformIds.All, platform =>
        {
            Assert.True(provider.IsSupported(platform));
            Assert.Equal(platform, provider.Create(platform).Platform);
        });
        Assert.Equal(7, PlatformIds.All.Count);
    }

    [Fact]
    public async Task Provider_specific_validation_returns_readable_failure_without_network()
    {
        var provider = new RealGitServiceProvider();
        var bitbucket = await provider.Create(PlatformIds.Bitbucket).ProbeAsync(
            new AccountCredential { Platform = PlatformIds.Bitbucket, Token = "secret" }, CancellationToken.None);
        var gitea = await provider.Create(PlatformIds.Gitea).ProbeAsync(
            new AccountCredential { Platform = PlatformIds.Gitea }, CancellationToken.None);

        Assert.False(bitbucket.Success);
        Assert.Contains("Username", bitbucket.Message);
        Assert.False(gitea.Success);
        Assert.Contains("Token", gitea.Message);
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData(0L, "0 KB")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1572864L, "1.5 MB")]
    public void Repository_size_is_formatted_from_bytes(long? bytes, string expected) =>
        Assert.Equal(expected, RepositoryInfo.FormatSize(bytes));

    [Fact]
    public void Git_log_mask_removes_password_from_authenticated_urls()
    {
        var output = GitHelper.Mask("fatal: https://user:top-secret@example.com/org/repo.git failed");

        Assert.DoesNotContain("top-secret", output);
        Assert.Contains("https://user:***@example.com", output);
    }

    [Fact]
    public async Task Encrypted_store_round_trips_accounts_without_plaintext_token()
    {
        var root = Path.Combine(Path.GetTempPath(), $"github-mirror-store-test-{Guid.NewGuid():N}");
        var store = new EncryptedCredentialStore(root);
        var token = "ghp_plaintext-must-not-appear";
        try
        {
            await store.SaveAsync(new[]
            {
                new AccountCredential
                {
                    Platform = PlatformIds.GitHub,
                    Label = "測試帳號",
                    Username = "octocat",
                    Token = token
                }
            }, CancellationToken.None);

            var loaded = await store.LoadAsync(CancellationToken.None);
            var fileBytes = await File.ReadAllBytesAsync(Path.Combine(root, "accounts.dat"));
            Assert.Single(loaded);
            Assert.Equal("octocat", loaded[0].Username);
            Assert.Equal(token, loaded[0].Token);
            Assert.DoesNotContain(token, Encoding.UTF8.GetString(fileBytes));
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Fact]
    public async Task Mirror_copies_branches_and_tags_but_drops_provider_refs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"github-mirror-test-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var target = Path.Combine(root, "target.git");
        Directory.CreateDirectory(source);
        try
        {
            await Git(source, "init");
            await Git(source, "config", "user.email", "mirror-tests@example.invalid");
            await Git(source, "config", "user.name", "Mirror Tests");
            await File.WriteAllTextAsync(Path.Combine(source, "README.md"), "mirror test");
            await Git(source, "add", "README.md");
            await Git(source, "commit", "-m", "initial");
            await Git(source, "branch", "feature/service-layer");
            await Git(source, "tag", "v1.0.0");
            await Git(source, "update-ref", "refs/pull/1/head", "HEAD");
            Directory.CreateDirectory(target);
            await Git(target, "init", "--bare");

            var sourceUrl = new Uri(source + Path.DirectorySeparatorChar).AbsoluteUri;
            var targetUrl = new Uri(target + Path.DirectorySeparatorChar).AbsoluteUri;
            await new GitMirrorRunner().RunAsync(sourceUrl, targetUrl, null, null, CancellationToken.None);

            var refs = await Git(root, "--git-dir", target, "show-ref");
            Assert.Contains("refs/heads/feature/service-layer", refs);
            Assert.Contains("refs/tags/v1.0.0", refs);
            Assert.DoesNotContain("refs/pull/", refs);
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static async Task<string> Git(string workingDirectory, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Unable to start git");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
        return output;
    }

    private static void DeleteTree(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }
}
