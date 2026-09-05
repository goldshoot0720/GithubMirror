using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GithubMirror.Models;

namespace GithubMirror.Services;

public sealed class BackupData
{
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<AccountCredential> Accounts { get; set; } = new();
    public bool HideTutorial { get; set; }
    public string NameTemplate { get; set; } = "{name}";
    public bool KeepPrivate { get; set; } = true;
    public bool AutoPlayMusic { get; set; } = true;
    public bool ContinuePlayback { get; set; } = true;
    public string MusicSourceUrl { get; set; } = string.Empty;
    public bool IncludeCollaboratorRepos { get; set; }
    public bool IncludeAllAccessibleRepos { get; set; }
    public bool ShowAllCommitSizes { get; set; }
}

public static class EncryptedBackup
{
    public const int MaximumFileSize = 8 * 1024 * 1024;
    private const int Iterations = 600_000;
    private static readonly byte[] Header = Encoding.ASCII.GetBytes("GMBACK01");
    private const int PrefixSize = 8 + 16 + 12 + 16;

    public static byte[] Encrypt(BackupData data, string password)
    {
        RequireFourDigitPin(password);
        Validate(data);
        var clear = JsonSerializer.SerializeToUtf8Bytes(data);
        byte[]? key = null;
        try
        {
            if (clear.Length > MaximumFileSize - PrefixSize) throw new InvalidDataException("備份資料過大。");
            var result = new byte[PrefixSize + clear.Length];
            Header.CopyTo(result, 0);
            RandomNumberGenerator.Fill(result.AsSpan(8, 16));
            RandomNumberGenerator.Fill(result.AsSpan(24, 12));
            key = Rfc2898DeriveBytes.Pbkdf2(password, result.AsSpan(8, 16), Iterations, HashAlgorithmName.SHA256, 32);
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(result.AsSpan(24, 12), clear, result.AsSpan(PrefixSize), result.AsSpan(36, 16), result.AsSpan(0, 24));
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
        }
    }

    public static BackupData Decrypt(byte[] payload, string password)
    {
        RequireFourDigitPin(password);
        if (payload.Length < PrefixSize || payload.Length > MaximumFileSize || !payload.AsSpan(0, 8).SequenceEqual(Header))
            throw new InvalidDataException("不是支援的 GithubMirror 加密備份，或檔案已損毀。");
        var clear = new byte[payload.Length - PrefixSize];
        var key = Rfc2898DeriveBytes.Pbkdf2(password, payload.AsSpan(8, 16), Iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(payload.AsSpan(24, 12), payload.AsSpan(PrefixSize), payload.AsSpan(36, 16), clear, payload.AsSpan(0, 24));
            var data = JsonSerializer.Deserialize<BackupData>(clear) ?? throw new InvalidDataException("備份內容為空。");
            Validate(data);
            return data;
        }
        catch (CryptographicException)
        {
            throw new InvalidDataException("密碼不正確或備份已遭修改，未還原任何資料。");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    internal static void RequireFourDigitPin(string password)
    {
        if (password is not { Length: 4 } || password.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("備份密碼須為四位數字。");
    }

    internal static void Validate(BackupData data)
    {
        if (data.Version != 1 || data.Accounts is null || data.Accounts.Count > 1000 ||
            data.NameTemplate is null || data.NameTemplate.Length > 500 || data.MusicSourceUrl is null ||
            data.Accounts.Any(a => a is null || string.IsNullOrWhiteSpace(a.Id) || !PlatformIds.All.Contains(a.Platform) ||
                a.Token is null || a.Username is null || a.ApiUrl is null || a.Organization is null || a.Project is null ||
                a.Region is null || a.Label is null || a.GitHttpsUsername is null || a.GitHttpsPassword is null) ||
            data.Accounts.Select(a => a.Id).Distinct().Count() != data.Accounts.Count)
            throw new InvalidDataException("備份版本或設定內容不受支援。");
    }
}
