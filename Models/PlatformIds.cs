using System;
using System.Collections.Generic;

namespace GithubMirror.Models;

public static class PlatformIds
{
    public const string GitHub = "GitHub";
    public const string GitLab = "GitLab";
    public const string Bitbucket = "Bitbucket";
    public const string Codeberg = "Codeberg";
    public const string Gitea = "Gitea";
    public const string AwsCodeCommit = "AWS CodeCommit";
    public const string AzureRepos = "Azure Repos";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        GitHub, GitLab, Bitbucket, Codeberg, Gitea, AwsCodeCommit, AzureRepos
    };
}

/// <summary>
/// 每個平台在「新增帳號」時最少需要哪些欄位。
/// 設計原則：能自動抓的就不要問使用者。
/// </summary>
public sealed class PlatformDescriptor
{
    public required string Id { get; init; }

    /// <summary>主要輸入框的標題（通常就是 Token）。</summary>
    public required string TokenLabel { get; init; }

    public string TokenHint { get; init; } = string.Empty;

    /// <summary>能不能只憑 Token 就自動查出使用者名稱。</summary>
    public bool AutoDetectsUser { get; init; } = true;

    /// <summary>必填的額外欄位。</summary>
    public bool NeedsUsername { get; init; }
    public bool NeedsApiUrl { get; init; }
    public bool NeedsOrganization { get; init; }
    public bool NeedsRegion { get; init; }
    public bool NeedsGitHttpsCredentials { get; init; }

    public string UsernameLabel { get; init; } = "使用者名稱";
    public string ApiUrlLabel { get; init; } = "伺服器位址";
    public string ApiUrlHint { get; init; } = string.Empty;
    public string OrganizationLabel { get; init; } = "組織";
    public string OrganizationHint { get; init; } = string.Empty;
    public string ProjectLabel { get; init; } = "專案";
    public string ProjectHint { get; init; } = string.Empty;
    public bool ShowProject { get; init; }
    public bool ShowOptionalApiUrl { get; init; }
    public bool ShowOptionalOrganization { get; init; }

    /// <summary>取得 Token 的頁面。</summary>
    public string TokenUrl { get; init; } = string.Empty;

    /// <summary>「開啟頁面」按鈕上的文字。</summary>
    public string TokenUrlLabel { get; init; } = "開啟設定頁面";

    /// <summary>一句話說明，顯示在輸入框下方。</summary>
    public string Hint { get; init; } = string.Empty;

    /// <summary>取得 Token / 連上 API 的逐步操作說明，顯示在「?」說明面板。</summary>
    public string[] SetupSteps { get; init; } = Array.Empty<string>();

    /// <summary>補充提醒（自架站台、憑證有效期等），顯示在步驟下方。</summary>
    public string SetupNote { get; init; } = string.Empty;

    /// <summary>Token 開頭前綴，用來自動判斷平台。</summary>
    public string[] TokenPrefixes { get; init; } = Array.Empty<string>();

    public static IReadOnlyDictionary<string, PlatformDescriptor> Map { get; } =
        new Dictionary<string, PlatformDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            [PlatformIds.GitHub] = new PlatformDescriptor
            {
                Id = PlatformIds.GitHub,
                TokenLabel = "GitHub Personal Access Token",
                TokenHint = "ghp_... 或 github_pat_...",
                TokenPrefixes = new[] { "ghp_", "github_pat_", "gho_", "ghs_" },
                TokenUrl = "https://github.com/settings/tokens",
                ShowOptionalApiUrl = true,
                ApiUrlLabel = "GitHub Enterprise 位址（選填）",
                ApiUrlHint = "https://github.公司.com",
                ShowOptionalOrganization = true,
                OrganizationLabel = "建立目標 repo 時放到哪個組織（選填）",
                OrganizationHint = "留空 = 建在自己帳號下",
                TokenUrlLabel = "開啟 GitHub Token 頁面",
                Hint = "需要 repo 權限；要鏡像到組織請再勾選 admin:org。",
                SetupSteps = new[]
                {
                    "登入 github.com，點右上角頭像 → Settings。",
                    "左側選單捲到最底，點 Developer settings。",
                    "選 Personal access tokens → Tokens (classic) → Generate new token (classic)。",
                    "Note 填「GithubMirror」，Expiration 建議選 90 days。",
                    "Scopes 勾選 repo（整組，含私有專案）；要把目標建在組織底下再加勾 admin:org 的 write:org。",
                    "按最下方 Generate token，複製 ghp_ 開頭的字串（離開頁面就再也看不到）。",
                    "回到本程式貼進「2 · Token」欄位，按「驗證並加入」即可自動抓出帳號與專案。"
                },
                SetupNote = "改用 Fine-grained token 也可以：Repository access 選 All repositories，"
                    + "Permissions 給 Contents（Read and write）、Administration（Read and write）、Metadata（Read-only）。"
                    + "GitHub Enterprise 請在下方「進階設定」填 https://github.你的公司.com，程式會自動接上 /api/v3。"
            },
            [PlatformIds.GitLab] = new PlatformDescriptor
            {
                Id = PlatformIds.GitLab,
                TokenLabel = "GitLab Personal Access Token",
                TokenHint = "glpat-...",
                TokenPrefixes = new[] { "glpat-" },
                TokenUrl = "https://gitlab.com/-/user_settings/personal_access_tokens",
                ShowOptionalApiUrl = true,
                ApiUrlLabel = "自架 GitLab 位址（選填）",
                ApiUrlHint = "https://gitlab.公司.com",
                ShowOptionalOrganization = true,
                OrganizationLabel = "目標群組 namespace ID（選填）",
                OrganizationHint = "群組頁面上的數字 ID",
                TokenUrlLabel = "開啟 GitLab Token 頁面",
                Hint = "需要 api 權限。",
                SetupSteps = new[]
                {
                    "登入 gitlab.com，點右上角頭像 → Edit profile。",
                    "左側選單點 Access tokens → Add new token。",
                    "Token name 填「GithubMirror」，Expiration date 可留預設。",
                    "Scopes 勾選 api（這一項就包含讀寫專案與建立專案）。",
                    "按 Create personal access token，複製 glpat- 開頭的字串。",
                    "回到本程式貼進 Token 欄位，按「驗證並加入」。"
                },
                SetupNote = "自架 GitLab 請在「進階設定」填站台網址（例：https://gitlab.你的公司.com）。"
                    + "要把目標建在群組底下，填該群組頁面上的數字 Namespace ID。"
            },
            [PlatformIds.Bitbucket] = new PlatformDescriptor
            {
                Id = PlatformIds.Bitbucket,
                TokenLabel = "Bitbucket App Password",
                AutoDetectsUser = false,
                NeedsUsername = true,
                UsernameLabel = "Bitbucket 使用者名稱",
                TokenUrl = "https://bitbucket.org/account/settings/app-passwords/",
                ShowOptionalOrganization = true,
                OrganizationLabel = "Workspace（選填）",
                OrganizationHint = "留空 = 使用自己的 workspace",
                TokenUrlLabel = "開啟 Bitbucket App password 頁面",
                Hint = "需要 Repositories: Read + Write + Admin 權限。",
                SetupSteps = new[]
                {
                    "登入 bitbucket.org，點右上角頭像 → Personal settings。",
                    "左側點 App passwords → Create app password。",
                    "Label 填「GithubMirror」，Permissions 勾 Repositories 的 Read、Write、Admin。",
                    "按 Create，複製顯示出來的密碼（只會顯示一次）。",
                    "在本程式「Bitbucket 使用者名稱」填 username（不是 email），可在 Personal settings → Account settings 查到。",
                    "把剛剛的 App password 貼進 Token 欄位，按「驗證並加入」。"
                },
                SetupNote = "要鏡像到團隊 workspace，請在「進階設定」填該 workspace 的 ID。"
            },
            [PlatformIds.Codeberg] = new PlatformDescriptor
            {
                Id = PlatformIds.Codeberg,
                TokenLabel = "Codeberg Access Token",
                TokenUrl = "https://codeberg.org/user/settings/applications",
                ShowOptionalOrganization = true,
                OrganizationLabel = "組織（選填）",
                TokenUrlLabel = "開啟 Codeberg Token 頁面",
                Hint = "需要 repository 讀寫權限。",
                SetupSteps = new[]
                {
                    "登入 codeberg.org，點右上角頭像 → Settings。",
                    "左側點 Applications，找到 Manage Access Tokens。",
                    "Token Name 填「GithubMirror」。",
                    "Select permissions 把 repository 設為 Read and Write；要建到組織底下再把 organization 也設為 Read and Write。",
                    "按 Generate Token，複製出現的字串（只會顯示一次）。",
                    "回到本程式貼進 Token 欄位，按「驗證並加入」。"
                },
                SetupNote = "Codeberg 是 Gitea 架的服務，若你用的是自架 Gitea，請改選上面的 Gitea 平台。"
            },
            [PlatformIds.Gitea] = new PlatformDescriptor
            {
                Id = PlatformIds.Gitea,
                TokenLabel = "Gitea Access Token",
                NeedsApiUrl = true,
                ApiUrlLabel = "Gitea 伺服器位址",
                ApiUrlHint = "https://gitea.公司.com",
                ShowOptionalOrganization = true,
                OrganizationLabel = "組織（選填）",
                Hint = "需要 repository 讀寫權限。",
                SetupSteps = new[]
                {
                    "用瀏覽器開啟你的 Gitea 站台並登入。",
                    "點右上角頭像 → Settings → Applications → Manage Access Tokens。",
                    "Token Name 填「GithubMirror」，權限把 repository 設為 Read and Write（要建到組織再加 organization）。",
                    "按 Generate Token，複製出現的字串（只會顯示一次）。",
                    "回到本程式，先在「Gitea 伺服器位址」填站台網址（必填，例：https://gitea.你的公司.com）。",
                    "再貼上 Token，按「驗證並加入」。"
                },
                SetupNote = "位址填到網站根目錄即可，程式會自動接上 /api/v1；站台若用自簽憑證，請先讓這台電腦信任該憑證。"
            },
            [PlatformIds.AwsCodeCommit] = new PlatformDescriptor
            {
                Id = PlatformIds.AwsCodeCommit,
                TokenLabel = "AWS Secret Access Key",
                AutoDetectsUser = false,
                NeedsUsername = true,
                UsernameLabel = "AWS Access Key ID",
                NeedsRegion = true,
                NeedsGitHttpsCredentials = true,
                TokenUrl = "https://console.aws.amazon.com/iam/home#/users",
                TokenUrlLabel = "開啟 AWS IAM 使用者頁面",
                Hint = "列出/建立 repository 用 Access Key；git 推送另需 IAM 的「HTTPS Git 憑證」。",
                SetupSteps = new[]
                {
                    "登入 AWS Console，進入 IAM → Users，點選要使用的 IAM 使用者。",
                    "確認該使用者有 AWSCodeCommitPowerUser（或同等）權限。",
                    "切到 Security credentials 分頁 → Create access key，用途選 Command Line Interface (CLI)。",
                    "記下 Access Key ID 與 Secret access key（Secret 只會顯示一次）。",
                    "同一頁往下找 HTTPS Git credentials for AWS CodeCommit → Generate credentials，下載使用者名稱與密碼。",
                    "回到本程式：Access Key ID、Secret Access Key、AWS 區域（例：ap-northeast-1），以及 HTTPS Git 憑證兩格全部填好。",
                    "按「驗證並加入」。"
                },
                SetupNote = "AWS CodeCommit 是兩組憑證：Access Key 用來呼叫 API 列出／建立 repository，"
                    + "HTTPS Git 憑證才是 git push 用的帳密，兩者缺一不可。區域要選 repository 實際所在的區域。"
            },
            [PlatformIds.AzureRepos] = new PlatformDescriptor
            {
                Id = PlatformIds.AzureRepos,
                TokenLabel = "Azure DevOps Personal Access Token",
                AutoDetectsUser = false,
                NeedsOrganization = true,
                OrganizationLabel = "Organization",
                OrganizationHint = "dev.azure.com/【這一段】",
                ShowProject = true,
                ProjectLabel = "Project（選填）",
                ProjectHint = "留空 = 掃描全部專案",
                TokenUrl = "https://dev.azure.com",
                TokenUrlLabel = "開啟 Azure DevOps",
                Hint = "PAT 需要 Code (Read, Write & Manage) 權限。",
                SetupSteps = new[]
                {
                    "登入 dev.azure.com，點右上角 User settings（人像旁的齒輪）→ Personal access tokens。",
                    "按 New Token，Name 填「GithubMirror」，Organization 選你要鏡像的組織。",
                    "Scopes 選 Custom defined → 展開 Code → 勾選 Read, write, & manage。",
                    "按 Create，複製 Token（只會顯示一次）。",
                    "回到本程式，在「進階設定」的 Organization 填 dev.azure.com/【這一段】。",
                    "貼上 Token，按「驗證並加入」。"
                },
                SetupNote = "Project 留空會掃描該組織下所有專案；只想處理單一專案就填專案名稱。"
                    + "Azure DevOps 無法只憑 Token 查出帳號，所以 Organization 是必填。"
            }
        };

    public static PlatformDescriptor For(string platform)
        => Map.TryGetValue(platform ?? string.Empty, out var d) ? d : Map[PlatformIds.GitHub];

    /// <summary>依 Token 前綴猜平台，猜不到回傳 null。</summary>
    public static string? GuessPlatformFromToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var t = token.Trim();
        foreach (var d in Map.Values)
        {
            foreach (var prefix in d.TokenPrefixes)
            {
                if (t.StartsWith(prefix, StringComparison.Ordinal))
                    return d.Id;
            }
        }
        return null;
    }
}
