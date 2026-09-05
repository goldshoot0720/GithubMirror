using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.CodeCommit;
using Amazon.CodeCommit.Model;
using Amazon.Runtime;
using GithubMirror.Models;

namespace GithubMirror.Services.Platforms;

public sealed class AwsCodeCommitApiService : IGitService
{
    public string Platform => PlatformIds.AwsCodeCommit;

    public async Task<ProbeResult> ProbeAsync(AccountCredential credential, CancellationToken ct)
    {
        var error = Validate(credential, requireGitCredentials: false);
        if (error is not null) return ProbeResult.Fail(error);
        try
        {
            using var client = CreateClient(credential);
            await client.ListRepositoriesAsync(new ListRepositoriesRequest(), ct).ConfigureAwait(false);
            return ProbeResult.Ok(credential.Username, $"AWS {credential.Region}");
        }
        catch (AmazonServiceException ex)
        {
            return ProbeResult.Fail(FriendlyAwsError("驗證 AWS CodeCommit 憑證", ex));
        }
    }

    public async Task<IReadOnlyList<RepositoryInfo>> ListRepositoriesAsync(
        AccountCredential credential, IProgress<string>? progress, CancellationToken ct)
    {
        var validation = Validate(credential, requireGitCredentials: false);
        if (validation is not null) throw new GitPlatformException(validation);
        using var client = CreateClient(credential);
        var summaries = new List<RepositoryNameIdPair>();
        string? nextToken = null;
        var page = 1;
        do
        {
            progress?.Report($"正在讀取 AWS CodeCommit 專案（第 {page++} 頁）…");
            var response = await client.ListRepositoriesAsync(new ListRepositoriesRequest
            {
                NextToken = nextToken,
                SortBy = SortByEnum.RepositoryName,
                Order = OrderEnum.Ascending
            }, ct).ConfigureAwait(false);
            summaries.AddRange(response.Repositories);
            nextToken = response.NextToken;
        } while (!string.IsNullOrWhiteSpace(nextToken));

        var result = new List<RepositoryInfo>(summaries.Count);
        foreach (var batch in summaries.Chunk(25))
        {
            var response = await client.BatchGetRepositoriesAsync(new BatchGetRepositoriesRequest
            {
                RepositoryNames = batch.Select(x => x.RepositoryName).ToList()
            }, ct).ConfigureAwait(false);
            foreach (var item in response.Repositories)
            {
                result.Add(new RepositoryInfo
                {
                    Name = item.RepositoryName,
                    Owner = item.AccountId,
                    NativeId = item.RepositoryId,
                    CloneUrl = item.CloneUrlHttp,
                    WebUrl = $"https://{credential.Region}.console.aws.amazon.com/codesuite/codecommit/repositories/{Uri.EscapeDataString(item.RepositoryName)}/browse?region={Uri.EscapeDataString(credential.Region)}",
                    Description = item.RepositoryDescription ?? string.Empty,
                    DefaultBranch = item.DefaultBranch ?? string.Empty,
                    SizeInBytes = null,
                    IsPrivate = true,
                    LastUpdated = item.LastModifiedDate
                });
            }
        }
        progress?.Report($"AWS CodeCommit 讀取完成，共 {result.Count} 個專案（AWS API 不提供倉庫容量）。");
        return result;
    }

    public Task<string> BuildSourceUrlAsync(RepositoryInfo repo, AccountCredential credential, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var validation = Validate(credential, requireGitCredentials: true);
        if (validation is not null) throw new GitPlatformException(validation);
        return Task.FromResult(GitHelper.InjectAuth(repo.CloneUrl, credential.GitHttpsUsername, credential.GitHttpsPassword));
    }

    public async Task<TargetRepository> EnsureTargetAsync(
        AccountCredential credential, RepositoryInfo source, string targetName, bool isPrivate, CancellationToken ct)
    {
        var validation = Validate(credential, requireGitCredentials: true);
        if (validation is not null) throw new GitPlatformException(validation);
        targetName = GitHelper.SanitizeRepoName(targetName);
        using var client = CreateClient(credential);
        RepositoryMetadata metadata;
        var wasCreated = false;
        try
        {
            metadata = (await client.GetRepositoryAsync(new GetRepositoryRequest
            {
                RepositoryName = targetName
            }, ct).ConfigureAwait(false)).RepositoryMetadata;
        }
        catch (RepositoryDoesNotExistException)
        {
            var description = string.IsNullOrWhiteSpace(source.Description) ? $"Mirror of {source.FullName}" : source.Description;
            if (description.Length > 1000) description = description[..1000];
            metadata = (await client.CreateRepositoryAsync(new CreateRepositoryRequest
            {
                RepositoryName = targetName,
                RepositoryDescription = description
            }, ct).ConfigureAwait(false)).RepositoryMetadata;
            wasCreated = true;
        }
        catch (AmazonServiceException ex)
        {
            throw new GitPlatformException(FriendlyAwsError("建立 AWS CodeCommit repository", ex), ex);
        }

        return new TargetRepository
        {
            PushUrl = GitHelper.InjectAuth(metadata.CloneUrlHttp, credential.GitHttpsUsername, credential.GitHttpsPassword),
            WebUrl = $"https://{credential.Region}.console.aws.amazon.com/codesuite/codecommit/repositories/{Uri.EscapeDataString(targetName)}/browse?region={Uri.EscapeDataString(credential.Region)}",
            WasCreated = wasCreated
        };
    }

    private static AmazonCodeCommitClient CreateClient(AccountCredential credential)
    {
        try
        {
            var region = RegionEndpoint.GetBySystemName(credential.Region);
            return new AmazonCodeCommitClient(new BasicAWSCredentials(credential.Username, credential.Token), region);
        }
        catch (Exception ex)
        {
            throw new GitPlatformException($"AWS Region 或憑證格式不正確：{ex.Message}", ex);
        }
    }

    private static string? Validate(AccountCredential credential, bool requireGitCredentials)
    {
        if (string.IsNullOrWhiteSpace(credential.Username)) return "請輸入 AWS Access Key ID。";
        if (string.IsNullOrWhiteSpace(credential.Token)) return "請輸入 AWS Secret Access Key。";
        if (string.IsNullOrWhiteSpace(credential.Region)) return "請輸入 AWS Region。";
        if (requireGitCredentials && (string.IsNullOrWhiteSpace(credential.GitHttpsUsername) || string.IsNullOrWhiteSpace(credential.GitHttpsPassword)))
            return "CodeCommit Git 鏡像需要 IAM 使用者的 HTTPS Git 憑證。";
        return null;
    }

    private static string FriendlyAwsError(string operation, AmazonServiceException ex) => ex.ErrorCode switch
    {
        "UnrecognizedClientException" or "InvalidSignatureException" => $"{operation}：Access Key 或 Secret Access Key 無效。",
        "AccessDeniedException" => $"{operation}：IAM 權限不足。",
        _ => $"{operation}：{ex.Message}"
    };
}
