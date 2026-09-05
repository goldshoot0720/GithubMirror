using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GithubMirror.Models;

namespace GithubMirror.Services;

// ============================================================================
//  離線示範實作 —— 讓 UI 在服務層還沒接上之前就能完整跑起來、看得到畫面。
//  Codex 完成真實服務層後，只要在 Program.cs 換掉 AppServices 即可。
// ============================================================================

public sealed class SampleGitServiceProvider : IGitServiceProvider
{
    public bool IsSupported(string platform) => PlatformIds.All.Contains(platform);
    public IGitService Create(string platform) => new SampleGitService(platform);
}

public sealed class SampleGitService : IGitService
{
    private static readonly string[] Languages = { "C#", "TypeScript", "Python", "Go", "Rust", "Kotlin", "Shell" };
    private static readonly string[] Names =
    {
        "web-portal", "api-gateway", "design-system", "mobile-app", "data-pipeline",
        "infra-terraform", "docs-site", "auth-service", "billing-worker", "cli-tools",
        "notification-hub", "search-index", "image-resizer", "report-builder"
    };

    public string Platform { get; }

    public SampleGitService(string platform) => Platform = platform;

    public async Task<ProbeResult> ProbeAsync(AccountCredential credential, CancellationToken ct)
    {
        await Task.Delay(450, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(credential.Token))
            return ProbeResult.Fail("請先貼上 Token。");

        var name = !string.IsNullOrWhiteSpace(credential.Username)
            ? credential.Username
            : "demo-user";

        return ProbeResult.Ok(name, name, "（示範模式）已驗證");
    }

    public async Task<IReadOnlyList<RepositoryInfo>> ListRepositoriesAsync(
        AccountCredential credential, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report($"正在讀取 {Platform} 的專案清單…");
        await Task.Delay(600, ct).ConfigureAwait(false);

        var seed = Math.Abs(credential.Id.GetHashCode()) % 1000;
        var rng = new Random(seed);
        var count = rng.Next(6, Names.Length + 1);

        var repos = new List<RepositoryInfo>(count);
        for (var i = 0; i < count; i++)
        {
            var isPrivate = rng.Next(0, 3) == 0;
            repos.Add(new RepositoryInfo
            {
                Name = Names[i],
                Owner = string.IsNullOrWhiteSpace(credential.Username) ? "demo-user" : credential.Username,
                CloneUrl = $"https://example.com/{credential.Username}/{Names[i]}.git",
                WebUrl = $"https://example.com/{credential.Username}/{Names[i]}",
                Description = "（示範資料）服務層接上後會顯示真實描述",
                DefaultBranch = "main",
                SizeInBytes = (long)rng.Next(120, 900_000) * 1024,
                Stars = rng.Next(0, 480),
                Forks = rng.Next(0, 60),
                IsPrivate = isPrivate,
                LastUpdated = DateTime.UtcNow.AddDays(-rng.Next(0, 400)),
                Language = Languages[rng.Next(Languages.Length)],
                SourcePlatform = Platform,
                AccountId = credential.Id,
                AccountDisplay = credential.DisplayName
            });
        }

        progress?.Report($"完成，共 {repos.Count} 個專案。");
        return repos;
    }

    public Task<string> BuildSourceUrlAsync(RepositoryInfo repo, AccountCredential credential, CancellationToken ct)
        => Task.FromResult(repo.CloneUrl);

    public Task<TargetRepository> EnsureTargetAsync(
        AccountCredential credential, RepositoryInfo source, string targetName, bool isPrivate, CancellationToken ct)
        => Task.FromResult(new TargetRepository
        {
            PushUrl = $"https://example.com/{credential.Username}/{targetName}.git",
            WebUrl = $"https://example.com/{credential.Username}/{targetName}",
            WasCreated = true
        });
}

public sealed class SampleMirrorRunner : IMirrorRunner
{
    public async Task RunAsync(
        string sourceUrlWithAuth, string targetUrlWithAuth,
        IProgress<string>? log, IProgress<double>? percent, CancellationToken ct)
    {
        string[] steps =
        {
            "正在從來源 clone（--mirror）…",
            "Receiving objects:  35% (1240/3542)",
            "Receiving objects:  78% (2763/3542)",
            "Resolving deltas: 100% (912/912)",
            "清理平台專屬的唯讀 ref…",
            "正在推送到目標（--mirror）…",
            "Writing objects: 100% (3542/3542)",
            "鏡像完成。"
        };

        for (var i = 0; i < steps.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            log?.Report(steps[i]);
            percent?.Report((i + 1) * 100.0 / steps.Length);
            await Task.Delay(420, ct).ConfigureAwait(false);
        }
    }
}

public sealed class InMemoryCredentialStore : ICredentialStore
{
    private List<AccountCredential> _accounts = new();

    public Task<List<AccountCredential>> LoadAsync(CancellationToken ct)
        => Task.FromResult(_accounts.Select(a => a.Clone()).ToList());

    public Task SaveAsync(IEnumerable<AccountCredential> accounts, CancellationToken ct)
    {
        _accounts = accounts.Select(a => a.Clone()).ToList();
        return Task.CompletedTask;
    }
}
