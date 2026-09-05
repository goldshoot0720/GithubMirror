using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GithubMirror.Models;
using GithubMirror.Services;

namespace GithubMirror.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly List<RepositoryInfo> _allRepositories = [];
    private AccountCredential? _selectedSourceAccount;
    private string _searchText = string.Empty;
    private int _visibilityFilterIndex;
    private bool _isLoading;
    private string _statusMessage = "新增 GitHub 帳號後即可載入專案";
    private CancellationTokenSource? _loadCancellation;

    public MainViewModel()
    {
        Accounts.CollectionChanged += AccountsChanged;
    }

    public ObservableCollection<AccountCredential> Accounts { get; } = [];
    public ObservableCollection<RepositoryInfo> Repositories { get; } = [];
    public ObservableCollection<MirrorTask> MirrorTasks { get; } = [];
    public IReadOnlyList<string> VisibilityFilters { get; } = ["全部可見性", "公開", "私人"];

    public IEnumerable<AccountCredential> GitHubAccounts => Accounts.Where(x => x.Platform == "GitHub");
    public int AccountCount => Accounts.Count;
    public int GitHubAccountCount => Accounts.Count(x => x.Platform == "GitHub");
    public int RepositoryCount => Repositories.Count;
    public int SelectedCount => _allRepositories.Count(x => x.IsSelected);
    public string TotalSize => SizeFormatter.Format(Repositories.Sum(x => x.SizeInBytes));
    public bool HasRepositories => Repositories.Count > 0;
    public bool HasNoRepositories => !IsLoading && Repositories.Count == 0;

    public AccountCredential? SelectedSourceAccount
    {
        get => _selectedSourceAccount;
        set
        {
            if (!SetField(ref _selectedSourceAccount, value)) return;
            OnPropertyChanged(nameof(SourceScopeText));
        }
    }

    public string SourceScopeText => SelectedSourceAccount?.DisplayName ?? "所有 GitHub 帳號";

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty)) ApplyFilters();
        }
    }

    public int VisibilityFilterIndex
    {
        get => _visibilityFilterIndex;
        set
        {
            if (SetField(ref _visibilityFilterIndex, value)) ApplyFilters();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetField(ref _isLoading, value)) return;
            OnPropertyChanged(nameof(HasNoRepositories));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public void AddAccount(AccountCredential account)
    {
        Accounts.Add(account);
        StatusMessage = $"已加入 {account.DisplayName}；憑證只保留於本次工作階段";
    }

    public void SetStatus(string message) => StatusMessage = message;

    public void RemoveAccount(AccountCredential account)
    {
        Accounts.Remove(account);
        if (SelectedSourceAccount == account) SelectedSourceAccount = null;
        _allRepositories.RemoveAll(x => x.SourceAccount?.Id == account.Id);
        ApplyFilters();
        StatusMessage = $"已移除 {account.DisplayName}";
    }

    public async Task LoadRepositoriesAsync()
    {
        var sourceAccounts = SelectedSourceAccount is not null
            ? new[] { SelectedSourceAccount }
            : GitHubAccounts.ToArray();

        if (sourceAccounts.Length == 0)
        {
            StatusMessage = "請先新增至少一個 GitHub 來源帳號";
            return;
        }

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;

        IsLoading = true;
        StatusMessage = $"正在從 {sourceAccounts.Length} 個 GitHub 帳號載入專案…";
        try
        {
            var jobs = sourceAccounts.Select(account => LoadAccountAsync(account, cancellationToken)).ToArray();
            var results = await Task.WhenAll(jobs);
            cancellationToken.ThrowIfCancellationRequested();

            _allRepositories.Clear();
            foreach (var result in results.SelectMany(x => x.Repositories).OrderByDescending(x => x.LastUpdated))
            {
                result.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(RepositoryInfo.IsSelected)) OnPropertyChanged(nameof(SelectedCount));
                };
                _allRepositories.Add(result);
            }
            ApplyFilters();

            var errors = results.Where(x => x.Error is not null).ToArray();
            StatusMessage = errors.Length == 0
                ? $"已載入 {_allRepositories.Count} 個專案"
                : $"已載入 {_allRepositories.Count} 個專案；{errors.Length} 個帳號失敗：{string.Join("、", errors.Select(x => x.Account.Username))}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消前一次載入";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void SelectAllVisible(bool selected)
    {
        foreach (var repository in Repositories) repository.IsSelected = selected;
        OnPropertyChanged(nameof(SelectedCount));
    }

    public async Task MirrorAsync(
        IReadOnlyList<RepositoryInfo> repositories,
        AccountCredential targetAccount,
        string? singleRepositoryName,
        bool includeLfs,
        CancellationToken cancellationToken = default)
    {
        if (repositories.Count == 0)
        {
            StatusMessage = "請先選擇至少一個專案";
            return;
        }

        await GitHelper.EnsureGitAvailableAsync(cancellationToken);
        StatusMessage = $"開始鏡像 {repositories.Count} 個專案到 {targetAccount.DisplayName}";

        foreach (var repository in repositories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetName = repositories.Count == 1 && !string.IsNullOrWhiteSpace(singleRepositoryName)
                ? singleRepositoryName.Trim()
                : repository.Name;
            var task = new MirrorTask
            {
                SourceRepository = repository.FullName,
                SourceAccount = repository.SourceAccountName,
                TargetPlatform = targetAccount.Platform,
                TargetRepository = targetName,
                Status = MirrorStatus.InProgress,
                ProgressValue = 5,
                Message = "正在準備遠端倉庫…"
            };
            MirrorTasks.Insert(0, task);

            try
            {
                var sourceAccount = repository.SourceAccount ?? throw new InvalidOperationException("找不到來源帳號資料。");
                if (sourceAccount.Id == targetAccount.Id && string.Equals(repository.Name, targetName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("來源與目標是同一個倉庫，已停止以避免覆寫自己。");

                var sourceService = GitServiceFactory.Create(sourceAccount.Platform);
                var targetService = GitServiceFactory.Create(targetAccount.Platform);
                var sourceRemote = await sourceService.GetSourceRemoteAsync(repository, sourceAccount, cancellationToken);
                var targetRemote = await targetService.PrepareTargetRepositoryAsync(targetAccount, repository, targetName, cancellationToken);
                var progress = new Progress<MirrorProgress>(value =>
                {
                    task.ProgressValue = value.Percentage;
                    task.Message = value.Message;
                });
                await GitHelper.ExecuteMirrorAsync(sourceRemote, targetRemote, includeLfs, progress, cancellationToken);
                task.Status = MirrorStatus.Completed;
                task.CompletedAt = DateTimeOffset.Now;
                repository.IsSelected = false;
            }
            catch (OperationCanceledException)
            {
                task.Status = MirrorStatus.Cancelled;
                task.Message = "操作已取消";
                task.CompletedAt = DateTimeOffset.Now;
                throw;
            }
            catch (Exception ex)
            {
                task.Status = MirrorStatus.Failed;
                task.ErrorMessage = ex.Message;
                task.Message = ex.Message;
                task.CompletedAt = DateTimeOffset.Now;
            }
        }

        var failed = MirrorTasks.Count(x => x.Status == MirrorStatus.Failed);
        StatusMessage = failed == 0 ? "鏡像工作已完成" : $"鏡像工作完成；目前有 {failed} 筆失敗記錄";
    }

    private async Task<(AccountCredential Account, IReadOnlyList<RepositoryInfo> Repositories, Exception? Error)> LoadAccountAsync(
        AccountCredential account, CancellationToken cancellationToken)
    {
        try
        {
            var repositories = await GitServiceFactory.Create(account.Platform).GetRepositoriesAsync(account, cancellationToken);
            return (account, repositories, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (account, Array.Empty<RepositoryInfo>(), ex);
        }
    }

    private void ApplyFilters()
    {
        IEnumerable<RepositoryInfo> query = _allRepositories;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var text = SearchText.Trim();
            query = query.Where(x => x.FullName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                                     x.Description.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                                     x.PrimaryLanguage.Contains(text, StringComparison.OrdinalIgnoreCase));
        }
        query = VisibilityFilterIndex switch
        {
            1 => query.Where(x => !x.IsPrivate),
            2 => query.Where(x => x.IsPrivate),
            _ => query
        };

        Repositories.Clear();
        foreach (var repository in query) Repositories.Add(repository);
        OnPropertyChanged(nameof(RepositoryCount));
        OnPropertyChanged(nameof(TotalSize));
        OnPropertyChanged(nameof(HasRepositories));
        OnPropertyChanged(nameof(HasNoRepositories));
        OnPropertyChanged(nameof(SelectedCount));
    }

    private void AccountsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(AccountCount));
        OnPropertyChanged(nameof(GitHubAccountCount));
        OnPropertyChanged(nameof(GitHubAccounts));
    }
}
