using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GithubMirror.Models;
using GithubMirror.Services;

namespace GithubMirror.Views;

public partial class AddAccountWindow : Window
{
    public AccountCredential? Credential { get; private set; }

    public AddAccountWindow()
    {
        InitializeComponent();
        PlatformCombo.ItemsSource = GitServiceFactory.SupportedPlatforms;
        PlatformCombo.SelectedIndex = 0;
        ApplyPlatformUi("GitHub");
    }

    private void PlatformChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PlatformCombo.SelectedItem is string platform) ApplyPlatformUi(platform);
    }

    private void ApplyPlatformUi(string platform)
    {
        ProjectPanel.IsVisible = platform == "Azure Repos";
        AwsPanel.IsVisible = platform == "AWS CodeCommit";
        ApiPanel.IsVisible = platform is "GitHub" or "GitLab" or "Gitea" or "Azure Repos";
        OrganizationPanel.IsVisible = platform != "AWS CodeCommit";
        UsernamePanel.IsVisible = true;
        TokenPanel.IsVisible = true;

        UsernameLabel.Text = platform switch
        {
            "Bitbucket" => "Atlassian 帳號 Email／使用者名稱 *",
            "AWS CodeCommit" => "CodeCommit HTTPS Git 使用者名稱",
            "Azure Repos" => "使用者名稱（可留白）",
            _ => "使用者名稱 *"
        };
        TokenLabel.Text = platform switch
        {
            "Bitbucket" => "Bitbucket API Token *",
            "AWS CodeCommit" => "CodeCommit HTTPS Git 密碼",
            "Azure Repos" => "Azure DevOps PAT *",
            _ => "Personal Access Token *"
        };
        OrganizationLabel.Text = platform switch
        {
            "Bitbucket" => "Workspace（留白使用帳號名稱）",
            "GitLab" => "Namespace／Group（選填）",
            "Azure Repos" => "Azure DevOps 組織 *",
            _ => "目標組織（選填）"
        };
        ApiLabel.Text = platform switch
        {
            "GitHub" => "GitHub API 根網址（選填）",
            "GitLab" => "GitLab 伺服器網址（選填）",
            "Gitea" => "Gitea 伺服器網址 *",
            "Azure Repos" => "Azure DevOps 根網址（選填）",
            _ => "API 網址"
        };
        ApiUrlBox.Watermark = platform switch
        {
            "GitHub" => "https://api.github.com",
            "GitLab" => "https://gitlab.com",
            "Gitea" => "https://git.example.com",
            "Azure Repos" => "https://dev.azure.com",
            _ => ""
        };
        PlatformHelpText.Text = platform switch
        {
            "GitHub" => "可作為多帳號來源或鏡像目的地。Fine-grained Token 需具備 Repository contents 讀取／寫入與建立倉庫權限。",
            "GitLab" => "使用 API Token 建立目標 Project；Group 可填完整 namespace 路徑或數字 ID。",
            "Bitbucket" => "使用 Workspace 路徑建立倉庫；API Token 需包含 repository 讀取、寫入與管理權限。",
            "Codeberg" => "Codeberg 採 Gitea API；Token 需有 repository 讀寫權限。",
            "Gitea" => "請填伺服器根網址，程式會自動接上 /api/v1。",
            "AWS CodeCommit" => "建立倉庫使用本機 AWS CLI Profile；Git 推送使用 IAM 的 CodeCommit HTTPS Git 帳密，若已設定全域 credential helper 可留白。",
            "Azure Repos" => "必須指定 Organization 與 Project；PAT 需具 Code（讀取及管理）權限。",
            _ => string.Empty
        };
        ValidationText.Text = string.Empty;
    }

    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        var platform = PlatformCombo.SelectedItem as string ?? "GitHub";
        var account = new AccountCredential
        {
            Platform = platform,
            Alias = AliasBox.Text?.Trim() ?? string.Empty,
            Username = UsernameBox.Text?.Trim() ?? string.Empty,
            Token = TokenBox.Text ?? string.Empty,
            ApiUrl = ApiUrlBox.Text?.Trim() ?? string.Empty,
            Organization = OrganizationBox.Text?.Trim() ?? string.Empty,
            Project = ProjectBox.Text?.Trim() ?? string.Empty,
            Region = RegionBox.Text?.Trim() ?? string.Empty,
            Profile = ProfileBox.Text?.Trim() ?? string.Empty
        };

        var validation = Validate(account);
        if (validation is not null)
        {
            ValidationText.Text = validation;
            return;
        }

        if (string.IsNullOrWhiteSpace(account.Alias))
            account.Alias = platform == "AWS CodeCommit"
                ? $"AWS {account.Region}"
                : string.IsNullOrWhiteSpace(account.Organization) ? account.Username : account.Organization;
        Credential = account;
        Close(true);
    }

    private static string? Validate(AccountCredential account)
    {
        if (account.Platform == "AWS CodeCommit")
        {
            if (string.IsNullOrWhiteSpace(account.Region)) return "請輸入 AWS Region。";
            if (string.IsNullOrWhiteSpace(account.Username) != string.IsNullOrWhiteSpace(account.Token))
                return "CodeCommit HTTPS Git 使用者名稱與密碼必須同時填寫，或同時留白使用既有 credential helper。";
            return null;
        }
        if (account.Platform == "Azure Repos")
        {
            if (string.IsNullOrWhiteSpace(account.Organization)) return "請輸入 Azure DevOps 組織。";
            if (string.IsNullOrWhiteSpace(account.Project)) return "請輸入 Azure DevOps Project。";
            if (string.IsNullOrWhiteSpace(account.Token)) return "請輸入 Azure DevOps PAT。";
            return null;
        }
        if (string.IsNullOrWhiteSpace(account.Username)) return "請輸入使用者名稱。";
        if (string.IsNullOrWhiteSpace(account.Token)) return "請輸入 Token。";
        if (account.Platform == "Gitea" && string.IsNullOrWhiteSpace(account.ApiUrl)) return "請輸入 Gitea 伺服器網址。";
        if (!string.IsNullOrWhiteSpace(account.ApiUrl) && !Uri.TryCreate(account.ApiUrl, UriKind.Absolute, out _)) return "伺服器網址格式不正確。";
        return null;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
