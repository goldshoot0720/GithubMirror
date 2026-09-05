using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GithubMirror.Services;

/// <summary>包裝本機 git 指令。</summary>
public static class GitHelper
{
    /// <summary>clone --mirror 之後要清掉的唯讀 ref（推到別的平台會被拒絕）。</summary>
    private static readonly string[] HiddenRefNamespaces =
    {
        "refs/pull",            // GitHub
        "refs/merge-requests",  // GitLab
        "refs/pipelines",       // GitLab
        "refs/keep-around",     // GitLab
        "refs/environments",    // GitLab
        "refs/remotes"          // 保險
    };

    private static bool? _gitAvailable;

    public static async Task<bool> IsGitAvailableAsync()
    {
        if (_gitAvailable.HasValue) return _gitAvailable.Value;
        try
        {
            var (exit, _, _) = await RunRawAsync(Path.GetTempPath(), "--version", null, CancellationToken.None)
                .ConfigureAwait(false);
            _gitAvailable = exit == 0;
        }
        catch
        {
            _gitAvailable = false;
        }
        return _gitAvailable.Value;
    }

    /// <summary>
    /// 執行完整鏡像：clone --mirror → 清掉唯讀 ref → push --mirror。
    /// </summary>
    public static async Task MirrorAsync(
        string sourceUrlWithAuth,
        string targetUrlWithAuth,
        IProgress<string>? log,
        IProgress<double>? percent,
        CancellationToken ct)
    {
        if (!await IsGitAvailableAsync().ConfigureAwait(false))
            throw new InvalidOperationException("找不到 git 指令，請先安裝 Git 並確認它在 PATH 中。");

        var work = Path.Combine(Path.GetTempPath(), "githubmirror", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        try
        {
            percent?.Report(5);
            log?.Report("正在從來源 clone（--mirror）…");
            await RunAsync(work, $"clone --mirror --progress \"{sourceUrlWithAuth}\" .", log, ct)
                .ConfigureAwait(false);

            percent?.Report(60);
            log?.Report("清理平台專屬的唯讀 ref…");
            await PruneHiddenRefsAsync(work, log, ct).ConfigureAwait(false);

            percent?.Report(65);
            log?.Report("正在推送到目標（--mirror）…");
            await RunAsync(work, $"push --mirror --progress \"{targetUrlWithAuth}\"", log, ct)
                .ConfigureAwait(false);

            percent?.Report(100);
            log?.Report("鏡像完成。");
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    private static async Task PruneHiddenRefsAsync(string work, IProgress<string>? log, CancellationToken ct)
    {
        var (exit, stdout, _) = await RunRawAsync(work, "for-each-ref --format=%(refname)", null, ct)
            .ConfigureAwait(false);
        if (exit != 0) return;

        var toDelete = new List<string>();
        foreach (var line in stdout.Split('\n'))
        {
            var refName = line.Trim();
            if (refName.Length == 0) continue;

            foreach (var ns in HiddenRefNamespaces)
            {
                if (refName.StartsWith(ns + "/", StringComparison.Ordinal))
                {
                    toDelete.Add(refName);
                    break;
                }
            }
        }

        if (toDelete.Count == 0) return;

        // 用 update-ref --stdin 一次刪光，避免上千次行程呼叫
        var script = new StringBuilder();
        foreach (var r in toDelete)
            script.Append("delete ").Append(r).Append('\n');

        await RunWithStdinAsync(work, "update-ref --stdin", script.ToString(), ct).ConfigureAwait(false);
        log?.Report($"已略過 {toDelete.Count} 個無法推送的 ref（pull request / merge request 等）。");
    }

    public static async Task RunAsync(string workDir, string args, IProgress<string>? log, CancellationToken ct)
    {
        var (exit, _, stderr) = await RunRawAsync(workDir, args, log, ct).ConfigureAwait(false);
        if (exit != 0)
            throw new InvalidOperationException($"git 失敗（代碼 {exit}）：{Mask(stderr).Trim()}");
    }

    private static ProcessStartInfo BuildStartInfo(string workDir, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var a in SplitArgs(args))
            psi.ArgumentList.Add(a);

        // 絕對不要跳出互動式帳密視窗，否則 UI 會卡死
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_ASKPASS"] = "echo";
        psi.Environment["GCM_INTERACTIVE"] = "never";
        psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        psi.Environment["LC_ALL"] = "C";

        return psi;
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunRawAsync(
        string workDir, string args, IProgress<string>? log, CancellationToken ct)
    {
        using var process = new Process { StartInfo = BuildStartInfo(workDir, args), EnableRaisingEvents = true };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            stdout.AppendLine(e.Data);
            log?.Report(Mask(e.Data));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            stderr.AppendLine(e.Data);
            log?.Report(Mask(e.Data));   // git 的進度訊息走 stderr
        };

        if (!process.Start())
            throw new InvalidOperationException("無法啟動 git 行程。");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static async Task RunWithStdinAsync(string workDir, string args, string stdin, CancellationToken ct)
    {
        using var process = new Process { StartInfo = BuildStartInfo(workDir, args) };
        if (!process.Start())
            throw new InvalidOperationException("無法啟動 git 行程。");

        await process.StandardInput.WriteAsync(stdin.AsMemory(), ct).ConfigureAwait(false);
        process.StandardInput.Close();

        _ = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        _ = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>把 URL 裡的密碼換成 ***，避免 token 被寫進畫面或日誌。</summary>
    public static string Mask(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return Regex.Replace(input, @"(https?://)([^:/@\s]+):([^@\s]+)@", "$1$2:***@");
    }

    /// <summary>把帳密塞進 https URL。</summary>
    public static string InjectAuth(string url, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return url;

        var rest = url.Substring("https://".Length);
        var slash = rest.IndexOf('/');
        var authority = slash >= 0 ? rest[..slash] : rest;
        var tail = slash >= 0 ? rest[slash..] : string.Empty;

        // 移除既有的 user:pass@
        var at = authority.LastIndexOf('@');
        if (at >= 0) authority = authority[(at + 1)..];

        var user = string.IsNullOrEmpty(username) ? "git" : Uri.EscapeDataString(username);
        var pass = Uri.EscapeDataString(password ?? string.Empty);
        return $"https://{user}:{pass}@{authority}{tail}";
    }

    /// <summary>把 repo 名稱正規化成各平台都能接受的形式。</summary>
    public static string SanitizeRepoName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "repository";
        var cleaned = Regex.Replace(name.Trim(), @"[^A-Za-z0-9._-]", "-");
        cleaned = Regex.Replace(cleaned, "-{2,}", "-").Trim('-', '.');
        if (cleaned.Length == 0) cleaned = "repository";
        return cleaned.Length > 100 ? cleaned[..100] : cleaned;
    }

    private static IEnumerable<string> SplitArgs(string commandLine)
    {
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    private static void TryDeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!Directory.Exists(path)) return;
                ClearReadOnly(new DirectoryInfo(path));
                Directory.Delete(path, recursive: true);
                return;
            }
            catch
            {
                Thread.Sleep(200);
            }
        }
    }

    private static void ClearReadOnly(DirectoryInfo dir)
    {
        try
        {
            foreach (var file in dir.GetFiles()) file.Attributes = FileAttributes.Normal;
            foreach (var sub in dir.GetDirectories()) ClearReadOnly(sub);
        }
        catch
        {
            // best effort
        }
    }
}
