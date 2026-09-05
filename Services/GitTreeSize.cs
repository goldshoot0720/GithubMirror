using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GithubMirror.Models;

namespace GithubMirror.Services;

/// <summary>把 git tree JSON 加總成「目前工作樹／最近一次提交」的位元組數。</summary>
public static class GitTreeSize
{
    public const int MaxConcurrent = 6;

    public static long? SumBlobBytes(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("tree", out var tree) ||
            tree.ValueKind != JsonValueKind.Array)
            return null;

        long total = 0;
        foreach (var item in tree.EnumerateArray())
        {
            if (!string.Equals(Rest.Str(item, "type"), "blob", StringComparison.OrdinalIgnoreCase))
                continue;
            total = checked(total + (Rest.Long(item, "size") ?? 0));
        }
        return total;
    }

    public static async Task<long?> FetchRecursiveTreeSizeAsync(
        string url, Action<HttpRequestMessage> authorize, CancellationToken ct)
    {
        using var request = Rest.Get(url);
        authorize(request);
        var response = await Rest.TrySendAsync(request, ct).ConfigureAwait(false);
        if (response.Status == HttpStatusCode.NotFound) return 0;
        if (!response.Ok) return null;
        try
        {
            using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(response.Body) ? "{}" : response.Body);
            return SumBlobBytes(json.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task FillLatestCommitSizesAsync(
        IReadOnlyList<RepositoryInfo> repos,
        Func<RepositoryInfo, CancellationToken, Task<long?>> fetch,
        IProgress<string>? progress,
        string label,
        CancellationToken ct)
    {
        if (repos.Count == 0) return;
        using var gate = new SemaphoreSlim(MaxConcurrent);
        var done = 0;
        await Task.WhenAll(repos.Select(async repo =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
                repo.LatestCommitSizeInBytes = await fetch(repo, ct).ConfigureAwait(false);
                repo.ApplySizeMode(showAllCommitSizes: false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is GitPlatformException or HttpRequestException or JsonException or OverflowException)
            {
                // 單一倉庫失敗時仍顯示清單，大小欄保持歷史值或「—」。
            }
            finally
            {
                gate.Release();
                var n = Interlocked.Increment(ref done);
                progress?.Report($"{label}（{n}/{repos.Count}）…");
            }
        })).ConfigureAwait(false);
    }
}
