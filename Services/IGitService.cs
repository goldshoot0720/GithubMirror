using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GithubMirror.Models;

namespace GithubMirror.Services;

// ============================================================================
//  UI ↔ 服務層 契約
//  UI 層（Views / ViewModels）只認得這個檔案裡的介面，
//  服務層（各平台實作）只要提供 IGitServiceProvider 與 ICredentialStore 即可。
// ============================================================================

/// <summary>Token 驗證結果；成功時回填自動偵測到的使用者名稱。</summary>
public sealed class ProbeResult
{
    public bool Success { get; init; }
    public string ResolvedUsername { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public static ProbeResult Ok(string username, string? display = null, string? message = null) => new()
    {
        Success = true,
        ResolvedUsername = username,
        DisplayName = string.IsNullOrWhiteSpace(display) ? username : display!,
        Message = message ?? string.Empty
    };

    public static ProbeResult Fail(string message) => new() { Success = false, Message = message };
}

/// <summary>建立 / 確認目標 repository 的結果。</summary>
public sealed class TargetRepository
{
    public required string PushUrl { get; init; }
    public string WebUrl { get; init; } = string.Empty;
    public bool WasCreated { get; init; }
}

public interface IGitService
{
    string Platform { get; }

    /// <summary>驗證憑證是否可用，並盡量自動補齊使用者名稱（少一個欄位要填）。</summary>
    Task<ProbeResult> ProbeAsync(AccountCredential credential, CancellationToken ct);

    /// <summary>列出這個帳號可存取的所有 repository（含大小）。</summary>
    Task<IReadOnlyList<RepositoryInfo>> ListRepositoriesAsync(
        AccountCredential credential, IProgress<string>? progress, CancellationToken ct);

    /// <summary>組出含帳密的來源 clone 網址。</summary>
    Task<string> BuildSourceUrlAsync(RepositoryInfo repo, AccountCredential credential, CancellationToken ct);

    /// <summary>確保目標 repository 存在（不存在就建立），回傳含帳密的 push 網址。</summary>
    Task<TargetRepository> EnsureTargetAsync(
        AccountCredential credential, RepositoryInfo source, string targetName, bool isPrivate, CancellationToken ct);
}

/// <summary>依平台代號取得服務實作。服務層負責提供。</summary>
public interface IGitServiceProvider
{
    IGitService Create(string platform);
    bool IsSupported(string platform);
}

/// <summary>帳號憑證的持久化。服務層負責提供（加密存本機）。</summary>
public interface ICredentialStore
{
    Task<List<AccountCredential>> LoadAsync(CancellationToken ct);
    Task SaveAsync(IEnumerable<AccountCredential> accounts, CancellationToken ct);
}

/// <summary>執行一次鏡像（clone --mirror → push --mirror）。服務層負責提供。</summary>
public interface IMirrorRunner
{
    Task RunAsync(
        string sourceUrlWithAuth,
        string targetUrlWithAuth,
        IProgress<string>? log,
        IProgress<double>? percent,
        CancellationToken ct);
}

/// <summary>整個 App 用到的服務集合，方便一次注入。</summary>
public sealed class AppServices
{
    public required IGitServiceProvider Git { get; init; }
    public required ICredentialStore Credentials { get; init; }
    public required IMirrorRunner Mirror { get; init; }

    /// <summary>服務層尚未接上時使用的離線示範資料。</summary>
    public static AppServices CreateSample() => new()
    {
        Git = new SampleGitServiceProvider(),
        Credentials = new InMemoryCredentialStore(),
        Mirror = new SampleMirrorRunner()
    };
}
