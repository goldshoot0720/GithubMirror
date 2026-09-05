using System;
using System.Text.Json.Serialization;

namespace GithubMirror.Models;

/// <summary>
/// 一組平台帳號憑證。整份清單會加密後存到本機 accounts.dat。
/// </summary>
public class AccountCredential : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _platform = PlatformIds.GitHub;
    private string _label = string.Empty;
    private string _username = string.Empty;
    private string _token = string.Empty;
    private string _apiUrl = string.Empty;
    private string _organization = string.Empty;
    private string _project = string.Empty;
    private string _region = string.Empty;
    private string _gitHttpsUsername = string.Empty;
    private string _gitHttpsPassword = string.Empty;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    /// <summary>平台代號，見 <see cref="PlatformIds"/>。</summary>
    public string Platform
    {
        get => _platform;
        set { if (SetProperty(ref _platform, value)) OnPropertyChanged(nameof(DisplayName)); }
    }

    /// <summary>使用者自訂的暱稱，例如「公司帳號」。</summary>
    public string Label
    {
        get => _label;
        set { if (SetProperty(ref _label, value)) OnPropertyChanged(nameof(DisplayName)); }
    }

    /// <summary>
    /// 使用者名稱。AWS CodeCommit 放 Access Key ID；Azure Repos 可留空。
    /// </summary>
    public string Username
    {
        get => _username;
        set { if (SetProperty(ref _username, value)) OnPropertyChanged(nameof(DisplayName)); }
    }

    /// <summary>
    /// 存取權杖。AWS CodeCommit 放 Secret Access Key；其餘平台放 PAT / App Password。
    /// </summary>
    public string Token
    {
        get => _token;
        set => SetProperty(ref _token, value);
    }

    /// <summary>自架服務的 API 位址（GitHub Enterprise / 自架 GitLab / Gitea）。</summary>
    public string ApiUrl
    {
        get => _apiUrl;
        set => SetProperty(ref _apiUrl, value);
    }

    /// <summary>組織 / 群組 / Bitbucket workspace。建立目標 repo 時使用。</summary>
    public string Organization
    {
        get => _organization;
        set { if (SetProperty(ref _organization, value)) OnPropertyChanged(nameof(DisplayName)); }
    }

    /// <summary>Azure DevOps 專案名稱。</summary>
    public string Project
    {
        get => _project;
        set => SetProperty(ref _project, value);
    }

    /// <summary>AWS 區域，例如 ap-northeast-1。</summary>
    public string Region
    {
        get => _region;
        set => SetProperty(ref _region, value);
    }

    /// <summary>AWS CodeCommit 的 HTTPS Git 憑證使用者名稱（與 Access Key 不同）。</summary>
    public string GitHttpsUsername
    {
        get => _gitHttpsUsername;
        set => SetProperty(ref _gitHttpsUsername, value);
    }

    /// <summary>AWS CodeCommit 的 HTTPS Git 憑證密碼。</summary>
    public string GitHttpsPassword
    {
        get => _gitHttpsPassword;
        set => SetProperty(ref _gitHttpsPassword, value);
    }

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var who = !string.IsNullOrWhiteSpace(Label)
                ? Label
                : !string.IsNullOrWhiteSpace(Username)
                    ? Username
                    : !string.IsNullOrWhiteSpace(Organization)
                        ? Organization
                        : "(未命名)";

            var org = !string.IsNullOrWhiteSpace(Organization) && Organization != who ? $" · {Organization}" : string.Empty;
            return $"[{Platform}] {who}{org}";
        }
    }

    public AccountCredential Clone() => new()
    {
        Id = Id,
        Platform = Platform,
        Label = Label,
        Username = Username,
        Token = Token,
        ApiUrl = ApiUrl,
        Organization = Organization,
        Project = Project,
        Region = Region,
        GitHttpsUsername = GitHttpsUsername,
        GitHttpsPassword = GitHttpsPassword
    };

    public void CopyFrom(AccountCredential other)
    {
        Platform = other.Platform;
        Label = other.Label;
        Username = other.Username;
        Token = other.Token;
        ApiUrl = other.ApiUrl;
        Organization = other.Organization;
        Project = other.Project;
        Region = other.Region;
        GitHttpsUsername = other.GitHttpsUsername;
        GitHttpsPassword = other.GitHttpsPassword;
    }

    public override string ToString() => DisplayName;
}
