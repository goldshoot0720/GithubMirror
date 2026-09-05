using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GithubMirror.Models;

namespace GithubMirror.Services;

public static class RealAppServices
{
    public static AppServices Create() => new()
    {
        Git = new RealGitServiceProvider(),
        Credentials = new EncryptedCredentialStore(),
        Mirror = new GitMirrorRunner()
    };
}

public sealed class RealGitServiceProvider : IGitServiceProvider
{
    private static readonly IReadOnlyDictionary<string, Func<IGitService>> Factories =
        new Dictionary<string, Func<IGitService>>(StringComparer.OrdinalIgnoreCase)
        {
            [PlatformIds.GitHub] = static () => new Platforms.GitHubApiService(),
            [PlatformIds.GitLab] = static () => new Platforms.GitLabApiService(),
            [PlatformIds.Bitbucket] = static () => new Platforms.BitbucketApiService(),
            [PlatformIds.Codeberg] = static () => new Platforms.CodebergApiService(),
            [PlatformIds.Gitea] = static () => new Platforms.GiteaApiService(),
            [PlatformIds.AwsCodeCommit] = static () => new Platforms.AwsCodeCommitApiService(),
            [PlatformIds.AzureRepos] = static () => new Platforms.AzureReposApiService()
        };

    public bool IsSupported(string platform) => !string.IsNullOrWhiteSpace(platform) && Factories.ContainsKey(platform);

    public IGitService Create(string platform) =>
        Factories.TryGetValue(platform ?? string.Empty, out var factory)
            ? factory()
            : throw new NotSupportedException($"尚未支援 Git 平台：{platform}");
}

public sealed class GitMirrorRunner : IMirrorRunner
{
    public Task RunAsync(
        string sourceUrlWithAuth,
        string targetUrlWithAuth,
        IProgress<string>? log,
        IProgress<double>? percent,
        CancellationToken ct) =>
        GitHelper.MirrorAsync(sourceUrlWithAuth, targetUrlWithAuth, log, percent, ct);
}

/// <summary>
/// 帳號以單一加密檔保存。Windows 使用目前使用者範圍的 DPAPI；其他系統使用
/// AES-GCM 與權限限制為目前使用者的本機金鑰檔。
/// </summary>
public sealed class EncryptedCredentialStore : ICredentialStore
{
    private const byte WindowsFormat = 1;
    private const byte AesGcmFormat = 2;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GithubMirror.Accounts.v1");
    private readonly string _directory;
    private readonly string _dataPath;
    private readonly string _keyPath;

    public EncryptedCredentialStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GithubMirror");
        _dataPath = Path.Combine(_directory, "accounts.dat");
        _keyPath = Path.Combine(_directory, "credentials.key");
    }

    public async Task<List<AccountCredential>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_dataPath)) return new List<AccountCredential>();

        try
        {
            var payload = await File.ReadAllBytesAsync(_dataPath, ct).ConfigureAwait(false);
            if (payload.Length < 2) throw new CryptographicException("憑證檔格式不完整。");
            var clear = payload[0] switch
            {
                WindowsFormat when OperatingSystem.IsWindows() =>
                    ProtectedData.Unprotect(payload[1..], Entropy, DataProtectionScope.CurrentUser),
                AesGcmFormat => await DecryptAesAsync(payload[1..], ct).ConfigureAwait(false),
                _ => throw new CryptographicException("憑證檔格式不受支援，或是由不同作業系統建立。")
            };

            return JsonSerializer.Deserialize<List<AccountCredential>>(clear) ?? new List<AccountCredential>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            throw new InvalidOperationException("無法解密已儲存的帳號。檔案可能損毀，或不屬於目前登入使用者。", ex);
        }
    }

    public async Task SaveAsync(IEnumerable<AccountCredential> accounts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        Directory.CreateDirectory(_directory);
        var clear = JsonSerializer.SerializeToUtf8Bytes(accounts.ToList());
        byte format;
        byte[] encrypted;

        if (OperatingSystem.IsWindows())
        {
            format = WindowsFormat;
            encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
        }
        else
        {
            format = AesGcmFormat;
            encrypted = await EncryptAesAsync(clear, ct).ConfigureAwait(false);
        }

        var payload = new byte[encrypted.Length + 1];
        payload[0] = format;
        Buffer.BlockCopy(encrypted, 0, payload, 1, encrypted.Length);
        var temporaryPath = Path.Combine(_directory, $"accounts.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, payload, ct).ConfigureAwait(false);
            File.Move(temporaryPath, _dataPath, overwrite: true);
            RestrictToCurrentUser(_dataPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private async Task<byte[]> EncryptAesAsync(byte[] clear, CancellationToken ct)
    {
        var key = await GetOrCreateKeyAsync(ct).ConfigureAwait(false);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[clear.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, clear, cipher, tag, Entropy);
        return [.. nonce, .. tag, .. cipher];
    }

    private async Task<byte[]> DecryptAesAsync(byte[] payload, CancellationToken ct)
    {
        if (payload.Length < 28) throw new CryptographicException("AES-GCM 憑證檔格式不完整。");
        var key = await GetOrCreateKeyAsync(ct, create: false).ConfigureAwait(false);
        var clear = new byte[payload.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(payload.AsSpan(0, 12), payload.AsSpan(28), payload.AsSpan(12, 16), clear, Entropy);
        return clear;
    }

    private async Task<byte[]> GetOrCreateKeyAsync(CancellationToken ct, bool create = true)
    {
        if (File.Exists(_keyPath))
        {
            var existing = await File.ReadAllBytesAsync(_keyPath, ct).ConfigureAwait(false);
            if (existing.Length != 32) throw new CryptographicException("本機憑證金鑰格式不正確。");
            return existing;
        }
        if (!create) throw new CryptographicException("找不到本機憑證金鑰。");

        var key = RandomNumberGenerator.GetBytes(32);
        await File.WriteAllBytesAsync(_keyPath, key, ct).ConfigureAwait(false);
        RestrictToCurrentUser(_keyPath);
        return key;
    }

    private static void RestrictToCurrentUser(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
