using GithubMirror.Models;

namespace GithubMirror.ViewModels;

/// <summary>左側帳號清單的一列（第一列是「全部帳號」）。</summary>
public sealed class AccountFilter : ObservableObject
{
    private int _repoCount;
    private bool _isLoading;
    private string _error = string.Empty;

    public AccountCredential? Account { get; }

    public AccountFilter(AccountCredential? account) => Account = account;

    public bool IsAll => Account is null;

    public string Platform => Account?.Platform ?? "ALL";

    public string Title => Account is null
        ? "全部帳號"
        : (!string.IsNullOrWhiteSpace(Account.Label) ? Account.Label
            : !string.IsNullOrWhiteSpace(Account.Username) ? Account.Username
            : !string.IsNullOrWhiteSpace(Account.Organization) ? Account.Organization
            : "(未命名)");

    public string Subtitle => Account is null ? "彙整所有平台" : Account.Platform;

    public int RepoCount
    {
        get => _repoCount;
        set
        {
            if (!SetProperty(ref _repoCount, value)) return;
            OnPropertyChanged(nameof(RepoCountDisplay));
            OnPropertyChanged(nameof(HasRepoCount));
        }
    }

    public string RepoCountDisplay => RepoCount > 0 ? RepoCount.ToString() : "";

    public bool HasRepoCount => RepoCount > 0;

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string Error
    {
        get => _error;
        set { if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(Platform));
    }
}

/// <summary>右側「鏡像到」的可勾選目標。</summary>
public sealed class TargetOption : ObservableObject
{
    private bool _isChecked;

    public AccountCredential Account { get; }

    public TargetOption(AccountCredential account) => Account = account;

    public string Title => !string.IsNullOrWhiteSpace(Account.Label) ? Account.Label
        : !string.IsNullOrWhiteSpace(Account.Username) ? Account.Username
        : !string.IsNullOrWhiteSpace(Account.Organization) ? Account.Organization
        : "(未命名)";

    public string Platform => Account.Platform;

    public string Subtitle => string.IsNullOrWhiteSpace(Account.Organization)
        ? Account.Platform
        : $"{Account.Platform} · {Account.Organization}";

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}
