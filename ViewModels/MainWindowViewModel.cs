using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GithubMirror.Models;
using GithubMirror.Services;

namespace GithubMirror.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly List<RepositoryInfo> _allRepositories = new();
    private CancellationTokenSource? _mirrorCts;

    public MainWindowViewModel() : this(AppServices.CreateSample()) { }

    public MainWindowViewModel(AppServices services)
    {
        _services = services;

        VisibilityFilters = new ObservableCollection<string> { "全部", "Public", "Private" };
        _visibilityFilter = VisibilityFilters[0];

        Platforms = new ObservableCollection<string>(PlatformIds.All.Where(_services.Git.IsSupported));
        _newPlatform = PlatformIds.GitHub;

        Repositories.CollectionChanged += OnRepositoriesChanged;

        AddAccountCommand = new AsyncRelayCommand(_ => AddAccountAsync(), _ => CanAddAccount);
        RemoveAccountCommand = new AsyncRelayCommand(p => RemoveAccountAsync(p as AccountCredential));
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => Accounts.Count > 0 && !IsLoading);
        SelectAllCommand = new RelayCommand(_ => SetAllSelected(true));
        SelectNoneCommand = new RelayCommand(_ => SetAllSelected(false));
        StartMirrorCommand = new AsyncRelayCommand(_ => StartMirrorAsync(), _ => CanStartMirror);
        CancelMirrorCommand = new RelayCommand(_ => _mirrorCts?.Cancel(), _ => IsMirroring);
        ClearFinishedJobsCommand = new RelayCommand(_ => ClearFinishedJobs(), _ => Jobs.Any(j => j.IsFinished));
        ToggleAddPanelCommand = new RelayCommand(_ => IsAddPanelOpen = !IsAddPanelOpen);
        DismissBannerCommand = new RelayCommand(_ => StatusMessage = string.Empty);
        ToggleSetupGuideCommand = new RelayCommand(_ => IsSetupGuideOpen = !IsSetupGuideOpen);
        OpenTokenPageCommand = new RelayCommand(_ => OpenTokenPage(), _ => HasTokenUrl);
        StartWithPlatformCommand = new RelayCommand(p => StartWithPlatform(p as string));
    }

    // =====================================================================
    //  帳號
    // =====================================================================

    public ObservableCollection<AccountCredential> Accounts { get; } = new();
    public ObservableCollection<AccountFilter> AccountFilters { get; } = new();
    public ObservableCollection<TargetOption> Targets { get; } = new();

    private AccountFilter? _selectedFilter;
    public AccountFilter? SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
                ApplyFilter();
        }
    }

    public bool HasAccounts => Accounts.Count > 0;
    public bool HasNoAccounts => Accounts.Count == 0;

    // =====================================================================
    //  專案清單
    // =====================================================================

    public ObservableCollection<RepositoryInfo> Repositories { get; } = new();

    public ObservableCollection<string> VisibilityFilters { get; }

    private string _visibilityFilter;
    public string VisibilityFilter
    {
        get => _visibilityFilter;
        set { if (SetProperty(ref _visibilityFilter, value)) ApplyFilter(); }
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsIdle => !IsLoading;

    public string RepoCountText => Repositories.Count == _allRepositories.Count
        ? $"{Repositories.Count} 個專案"
        : $"{Repositories.Count} / {_allRepositories.Count} 個專案";

    public string TotalSizeText
    {
        get
        {
            var known = Repositories.Where(r => r.SizeInBytes.HasValue).ToList();
            if (known.Count == 0) return "大小未知";
            var total = known.Sum(r => r.SizeInBytes!.Value);
            var suffix = known.Count == Repositories.Count ? "" : $"（{known.Count} 筆有大小資料）";
            return RepositoryInfo.FormatSize(total) + suffix;
        }
    }

    public int SelectedCount => Repositories.Count(r => r.IsSelected);

    public string SelectedCountText => SelectedCount == 0 ? "未選取" : $"已選 {SelectedCount} 個";

    public bool HasRepositories => Repositories.Count > 0;
    public bool HasNoRepositories => Repositories.Count == 0;

    // =====================================================================
    //  鏡像設定
    // =====================================================================

    private string _nameTemplate = "{name}";
    public string NameTemplate
    {
        get => _nameTemplate;
        set { if (SetProperty(ref _nameTemplate, value)) OnPropertyChanged(nameof(NamePreview)); }
    }

    public string NamePreview
    {
        get
        {
            var sample = Repositories.FirstOrDefault(r => r.IsSelected) ?? Repositories.FirstOrDefault();
            return sample is null ? "" : "→ " + BuildTargetName(sample);
        }
    }

    private bool _keepPrivate = true;
    public bool KeepPrivate
    {
        get => _keepPrivate;
        set => SetProperty(ref _keepPrivate, value);
    }

    private bool _isMirroring;
    public bool IsMirroring
    {
        get => _isMirroring;
        set
        {
            if (SetProperty(ref _isMirroring, value))
            {
                OnPropertyChanged(nameof(IsNotMirroring));
                StartMirrorCommand.RaiseCanExecuteChanged();
                CancelMirrorCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNotMirroring => !IsMirroring;

    public int CheckedTargetCount => Targets.Count(t => t.IsChecked);

    public bool CanStartMirror => !IsMirroring && SelectedCount > 0 && CheckedTargetCount > 0;

    public string StartButtonText => SelectedCount == 0 || CheckedTargetCount == 0
        ? "開始鏡像"
        : $"開始鏡像 {SelectedCount} × {CheckedTargetCount}";

    public ObservableCollection<MirrorJob> Jobs { get; } = new();

    public bool HasJobs => Jobs.Count > 0;

    // =====================================================================
    //  新增帳號（內嵌面板，不開新視窗）
    // =====================================================================

    public ObservableCollection<string> Platforms { get; }

    private bool _isAddPanelOpen;
    public bool IsAddPanelOpen
    {
        get => _isAddPanelOpen;
        set
        {
            if (SetProperty(ref _isAddPanelOpen, value) && value)
                AddError = string.Empty;
        }
    }

    private string _newPlatform;
    public string NewPlatform
    {
        get => _newPlatform;
        set
        {
            if (SetProperty(ref _newPlatform, value))
                RaiseDescriptorChanged();
        }
    }

    private string _newToken = string.Empty;
    public string NewToken
    {
        get => _newToken;
        set
        {
            if (!SetProperty(ref _newToken, value)) return;

            // 貼上 token 時自動判斷平台，少按一次下拉選單
            var guess = PlatformDescriptor.GuessPlatformFromToken(value);
            if (guess is not null && guess != NewPlatform)
                NewPlatform = guess;

            AddAccountCommand.RaiseCanExecuteChanged();
        }
    }

    private string _newUsername = string.Empty;
    public string NewUsername
    {
        get => _newUsername;
        set { if (SetProperty(ref _newUsername, value)) AddAccountCommand.RaiseCanExecuteChanged(); }
    }

    private string _newApiUrl = string.Empty;
    public string NewApiUrl
    {
        get => _newApiUrl;
        set { if (SetProperty(ref _newApiUrl, value)) AddAccountCommand.RaiseCanExecuteChanged(); }
    }

    private string _newOrganization = string.Empty;
    public string NewOrganization
    {
        get => _newOrganization;
        set { if (SetProperty(ref _newOrganization, value)) AddAccountCommand.RaiseCanExecuteChanged(); }
    }

    private string _newProject = string.Empty;
    public string NewProject
    {
        get => _newProject;
        set => SetProperty(ref _newProject, value);
    }

    private string _newRegion = "ap-northeast-1";
    public string NewRegion
    {
        get => _newRegion;
        set { if (SetProperty(ref _newRegion, value)) AddAccountCommand.RaiseCanExecuteChanged(); }
    }

    private string _newGitUser = string.Empty;
    public string NewGitUser
    {
        get => _newGitUser;
        set => SetProperty(ref _newGitUser, value);
    }

    private string _newGitPass = string.Empty;
    public string NewGitPass
    {
        get => _newGitPass;
        set => SetProperty(ref _newGitPass, value);
    }

    private bool _isAdvancedOpen;
    public bool IsAdvancedOpen
    {
        get => _isAdvancedOpen;
        set => SetProperty(ref _isAdvancedOpen, value);
    }

    private string _addError = string.Empty;
    public string AddError
    {
        get => _addError;
        set { if (SetProperty(ref _addError, value)) OnPropertyChanged(nameof(HasAddError)); }
    }

    public bool HasAddError => !string.IsNullOrEmpty(AddError);

    private bool _isVerifying;
    public bool IsVerifying
    {
        get => _isVerifying;
        set
        {
            if (SetProperty(ref _isVerifying, value))
                AddAccountCommand.RaiseCanExecuteChanged();
        }
    }

    public PlatformDescriptor Descriptor => PlatformDescriptor.For(NewPlatform);

    // 給 XAML 直接綁的欄位顯示旗標
    public bool ShowUsernameField => Descriptor.NeedsUsername;
    public bool ShowApiUrlField => Descriptor.NeedsApiUrl || Descriptor.ShowOptionalApiUrl;
    public bool ShowOrganizationField => Descriptor.NeedsOrganization || Descriptor.ShowOptionalOrganization;
    public bool ShowProjectField => Descriptor.ShowProject;
    public bool ShowRegionField => Descriptor.NeedsRegion;
    public bool ShowGitHttpsFields => Descriptor.NeedsGitHttpsCredentials;
    public bool ShowAdvancedToggle => ShowApiUrlField || ShowOrganizationField || ShowProjectField;

    public string TokenLabel => Descriptor.TokenLabel;
    public string TokenHint => Descriptor.TokenHint;
    public string PlatformHint => Descriptor.Hint;
    public string UsernameLabel => Descriptor.UsernameLabel;
    public string ApiUrlLabel => Descriptor.ApiUrlLabel;
    public string ApiUrlHint => Descriptor.ApiUrlHint;
    public string OrganizationLabel => Descriptor.OrganizationLabel;
    public string OrganizationHint => Descriptor.OrganizationHint;
    public string ProjectLabel => Descriptor.ProjectLabel;
    public string ProjectHint => Descriptor.ProjectHint;
    public string TokenUrl => Descriptor.TokenUrl;
    public bool HasTokenUrl => !string.IsNullOrEmpty(Descriptor.TokenUrl);
    public string TokenUrlLabel => Descriptor.TokenUrlLabel;

    // ---------------------------------------------------------------------
    //  取得 Token / 連上 API 的步驟說明（「?」圖示打開的面板）
    // ---------------------------------------------------------------------

    private bool _isSetupGuideOpen;
    public bool IsSetupGuideOpen
    {
        get => _isSetupGuideOpen;
        set => SetProperty(ref _isSetupGuideOpen, value);
    }

    public string SetupGuideTitle => $"如何取得 {NewPlatform} 的連線權限";

    public IReadOnlyList<SetupStep> SetupSteps => Descriptor.SetupSteps
        .Select((text, index) => new SetupStep(index + 1, text))
        .ToList();

    public string SetupNote => Descriptor.SetupNote;
    public bool HasSetupNote => !string.IsNullOrEmpty(Descriptor.SetupNote);

    public bool CanAddAccount
    {
        get
        {
            if (IsVerifying) return false;
            if (string.IsNullOrWhiteSpace(NewToken)) return false;
            var d = Descriptor;
            if (d.NeedsUsername && string.IsNullOrWhiteSpace(NewUsername)) return false;
            if (d.NeedsApiUrl && string.IsNullOrWhiteSpace(NewApiUrl)) return false;
            if (d.NeedsOrganization && string.IsNullOrWhiteSpace(NewOrganization)) return false;
            if (d.NeedsRegion && string.IsNullOrWhiteSpace(NewRegion)) return false;
            return true;
        }
    }

    private void RaiseDescriptorChanged()
    {
        foreach (var name in new[]
                 {
                     nameof(Descriptor), nameof(ShowUsernameField), nameof(ShowApiUrlField),
                     nameof(ShowOrganizationField), nameof(ShowProjectField), nameof(ShowRegionField),
                     nameof(ShowGitHttpsFields), nameof(ShowAdvancedToggle), nameof(TokenLabel),
                     nameof(TokenHint), nameof(PlatformHint), nameof(UsernameLabel), nameof(ApiUrlLabel),
                     nameof(ApiUrlHint), nameof(OrganizationLabel), nameof(OrganizationHint),
                     nameof(ProjectLabel), nameof(ProjectHint), nameof(TokenUrl), nameof(HasTokenUrl),
                     nameof(TokenUrlLabel), nameof(SetupGuideTitle), nameof(SetupSteps),
                     nameof(SetupNote), nameof(HasSetupNote), nameof(CanAddAccount)
                 })
        {
            OnPropertyChanged(name);
        }

        AddAccountCommand.RaiseCanExecuteChanged();
        OpenTokenPageCommand.RaiseCanExecuteChanged();
    }

    private void OpenTokenPage()
    {
        if (SystemBrowser.TryOpen(TokenUrl, out var error)) return;
        AddError = error;
    }

    /// <summary>空狀態的捷徑：直接切到指定平台、打開新增面板與步驟說明。</summary>
    private void StartWithPlatform(string? platform)
    {
        if (!string.IsNullOrWhiteSpace(platform) && Platforms.Contains(platform))
            NewPlatform = platform;

        IsAddPanelOpen = true;
        IsSetupGuideOpen = true;
    }

    // =====================================================================
    //  狀態列
    // =====================================================================

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set { if (SetProperty(ref _statusMessage, value)) OnPropertyChanged(nameof(HasStatusMessage)); }
    }

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    private bool _statusIsError;
    public bool StatusIsError
    {
        get => _statusIsError;
        set => SetProperty(ref _statusIsError, value);
    }

    // =====================================================================
    //  命令
    // =====================================================================

    public AsyncRelayCommand AddAccountCommand { get; }
    public AsyncRelayCommand RemoveAccountCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public AsyncRelayCommand StartMirrorCommand { get; }
    public RelayCommand CancelMirrorCommand { get; }
    public RelayCommand ClearFinishedJobsCommand { get; }
    public RelayCommand ToggleAddPanelCommand { get; }
    public RelayCommand DismissBannerCommand { get; }
    public RelayCommand ToggleSetupGuideCommand { get; }
    public RelayCommand OpenTokenPageCommand { get; }
    public RelayCommand StartWithPlatformCommand { get; }

    // =====================================================================
    //  行為
    // =====================================================================

    public async Task InitializeAsync()
    {
        try
        {
            var stored = await _services.Credentials.LoadAsync(CancellationToken.None).ConfigureAwait(true);
            foreach (var a in stored)
                Accounts.Add(a);
        }
        catch (Exception ex)
        {
            SetStatus($"讀取已存帳號失敗：{ex.Message}", isError: true);
        }

        RebuildAccountViews();

        if (Accounts.Count == 0)
        {
            // 第一次使用：新增面板與取得 Token 的步驟都先攤開，不用自己找。
            IsAddPanelOpen = true;
            IsSetupGuideOpen = true;
            SetStatus("先貼上一組 Token，程式會自動辨識平台並列出專案。", isError: false);
        }
        else
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    private async Task AddAccountAsync()
    {
        AddError = string.Empty;
        IsVerifying = true;

        var candidate = new AccountCredential
        {
            Platform = NewPlatform,
            Username = NewUsername.Trim(),
            Token = NewToken.Trim(),
            ApiUrl = NewApiUrl.Trim(),
            Organization = NewOrganization.Trim(),
            Project = NewProject.Trim(),
            Region = NewRegion.Trim(),
            GitHttpsUsername = NewGitUser.Trim(),
            GitHttpsPassword = NewGitPass
        };

        try
        {
            var service = _services.Git.Create(candidate.Platform);
            var probe = await service.ProbeAsync(candidate, CancellationToken.None).ConfigureAwait(true);

            if (!probe.Success)
            {
                AddError = probe.Message;
                return;
            }

            if (string.IsNullOrWhiteSpace(candidate.Username))
                candidate.Username = probe.ResolvedUsername;
            if (string.IsNullOrWhiteSpace(candidate.Label))
                candidate.Label = string.IsNullOrWhiteSpace(probe.DisplayName) ? probe.ResolvedUsername : probe.DisplayName;

            Accounts.Add(candidate);
            await SaveAccountsAsync().ConfigureAwait(true);

            RebuildAccountViews();
            ResetAddForm();
            IsAddPanelOpen = false;

            SelectedFilter = AccountFilters.FirstOrDefault(f => f.Account?.Id == candidate.Id);
            await LoadAccountAsync(candidate).ConfigureAwait(true);

            SetStatus($"已加入 {candidate.DisplayName}", isError: false);
        }
        catch (Exception ex)
        {
            AddError = ex.Message;
        }
        finally
        {
            IsVerifying = false;
        }
    }

    private async Task RemoveAccountAsync(AccountCredential? account)
    {
        if (account is null) return;

        Accounts.Remove(account);
        _allRepositories.RemoveAll(r => r.AccountId == account.Id);
        await SaveAccountsAsync().ConfigureAwait(true);

        RebuildAccountViews();
        ApplyFilter();
        SetStatus($"已移除 {account.DisplayName}", isError: false);
    }

    public async Task RefreshAsync()
    {
        var filter = SelectedFilter;
        var targets = filter is null || filter.IsAll
            ? Accounts.ToList()
            : new List<AccountCredential> { filter.Account! };

        if (targets.Count == 0) return;

        IsLoading = true;
        try
        {
            foreach (var account in targets)
                await LoadAccountAsync(account).ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadAccountAsync(AccountCredential account)
    {
        var row = AccountFilters.FirstOrDefault(f => f.Account?.Id == account.Id);
        if (row is not null)
        {
            row.IsLoading = true;
            row.Error = string.Empty;
        }

        try
        {
            var service = _services.Git.Create(account.Platform);
            var progress = new Progress<string>(msg => SetStatus(msg, isError: false));
            var repos = await service.ListRepositoriesAsync(account, progress, CancellationToken.None)
                .ConfigureAwait(true);

            _allRepositories.RemoveAll(r => r.AccountId == account.Id);
            foreach (var repo in repos)
            {
                repo.AccountId = account.Id;
                repo.AccountDisplay = account.DisplayName;
                repo.SourcePlatform = account.Platform;
                repo.PropertyChanged += OnRepositoryPropertyChanged;
                _allRepositories.Add(repo);
            }

            if (row is not null) row.RepoCount = repos.Count;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            if (row is not null) row.Error = ex.Message;
            SetStatus($"{account.DisplayName}：{ex.Message}", isError: true);
        }
        finally
        {
            if (row is not null) row.IsLoading = false;
        }
    }

    private async Task SaveAccountsAsync()
    {
        try
        {
            await _services.Credentials.SaveAsync(Accounts, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus($"儲存帳號失敗：{ex.Message}", isError: true);
        }
    }

    private void RebuildAccountViews()
    {
        var previousId = SelectedFilter?.Account?.Id;
        var wasAll = SelectedFilter?.IsAll ?? true;

        AccountFilters.Clear();
        if (Accounts.Count > 0)
            AccountFilters.Add(new AccountFilter(null));

        foreach (var a in Accounts)
        {
            var f = new AccountFilter(a) { RepoCount = _allRepositories.Count(r => r.AccountId == a.Id) };
            AccountFilters.Add(f);
        }

        Targets.Clear();
        foreach (var a in Accounts)
        {
            var option = new TargetOption(a);
            option.PropertyChanged += OnTargetChanged;
            Targets.Add(option);
        }

        SelectedFilter = wasAll
            ? AccountFilters.FirstOrDefault()
            : AccountFilters.FirstOrDefault(f => f.Account?.Id == previousId) ?? AccountFilters.FirstOrDefault();

        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(HasNoAccounts));
        OnPropertyChanged(nameof(CheckedTargetCount));
        RefreshCommand.RaiseCanExecuteChanged();
    }

    private void ApplyFilter()
    {
        IEnumerable<RepositoryInfo> query = _allRepositories;

        var filter = SelectedFilter;
        if (filter is { IsAll: false, Account: not null })
            query = query.Where(r => r.AccountId == filter.Account.Id);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var needle = SearchText.Trim();
            query = query.Where(r =>
                r.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                r.Owner.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                r.Description.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                r.Language.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        query = VisibilityFilter switch
        {
            "Public" => query.Where(r => !r.IsPrivate),
            "Private" => query.Where(r => r.IsPrivate),
            _ => query
        };

        Repositories.Clear();
        foreach (var repo in query.OrderByDescending(r => r.SizeInBytes ?? -1).ThenBy(r => r.Name))
            Repositories.Add(repo);

        RaiseListStats();
    }

    private void SetAllSelected(bool value)
    {
        foreach (var repo in Repositories)
            repo.IsSelected = value;
        RaiseSelectionStats();
    }

    private async Task StartMirrorAsync()
    {
        var repos = Repositories.Where(r => r.IsSelected).ToList();
        var targets = Targets.Where(t => t.IsChecked).Select(t => t.Account).ToList();
        if (repos.Count == 0 || targets.Count == 0) return;

        _mirrorCts = new CancellationTokenSource();
        var ct = _mirrorCts.Token;
        IsMirroring = true;

        try
        {
            foreach (var repo in repos)
            {
                foreach (var target in targets)
                {
                    if (ct.IsCancellationRequested) return;

                    var sourceAccount = Accounts.FirstOrDefault(a => a.Id == repo.AccountId);
                    if (sourceAccount is null) continue;

                    var job = new MirrorJob
                    {
                        SourceRepository = repo.Name,
                        SourcePlatform = repo.SourcePlatform,
                        SourceAccount = sourceAccount.DisplayName,
                        TargetPlatform = target.Platform,
                        TargetAccount = target.DisplayName,
                        TargetRepository = BuildTargetName(repo)
                    };
                    Jobs.Insert(0, job);
                    OnPropertyChanged(nameof(HasJobs));

                    await RunJobAsync(job, repo, sourceAccount, target, ct).ConfigureAwait(true);
                    ClearFinishedJobsCommand.RaiseCanExecuteChanged();
                }
            }

            var failed = Jobs.Count(j => j.Status == MirrorStatus.Failed);
            SetStatus(failed == 0 ? "全部鏡像完成。" : $"完成，但有 {failed} 個失敗，點任務可看紀錄。", failed > 0);
        }
        finally
        {
            IsMirroring = false;
            _mirrorCts?.Dispose();
            _mirrorCts = null;
        }
    }

    private async Task RunJobAsync(
        MirrorJob job, RepositoryInfo repo,
        AccountCredential sourceAccount, AccountCredential targetAccount, CancellationToken ct)
    {
        job.Status = MirrorStatus.Running;
        job.Step = "準備中";

        var log = new Progress<string>(line =>
        {
            job.AppendLog(line);
            job.Step = line.Length > 60 ? line[..60] + "…" : line;
        });
        var percent = new Progress<double>(p => job.Percent = p);

        try
        {
            var sourceService = _services.Git.Create(sourceAccount.Platform);
            var targetService = _services.Git.Create(targetAccount.Platform);

            job.Step = "取得來源網址";
            var sourceUrl = await sourceService.BuildSourceUrlAsync(repo, sourceAccount, ct).ConfigureAwait(true);

            job.Step = "建立/確認目標 repository";
            var isPrivate = KeepPrivate && repo.IsPrivate;
            var target = await targetService
                .EnsureTargetAsync(targetAccount, repo, job.TargetRepository, isPrivate, ct)
                .ConfigureAwait(true);
            job.AppendLog(target.WasCreated ? $"已建立目標：{target.WebUrl}" : $"目標已存在：{target.WebUrl}");

            await _services.Mirror.RunAsync(sourceUrl, target.PushUrl, log, percent, ct).ConfigureAwait(true);

            job.Status = MirrorStatus.Completed;
            job.Percent = 100;
            job.Step = "完成";
        }
        catch (OperationCanceledException)
        {
            job.Status = MirrorStatus.Cancelled;
            job.Step = "已取消";
        }
        catch (Exception ex)
        {
            job.Status = MirrorStatus.Failed;
            job.Step = "失敗";
            job.ErrorMessage = ex.Message;
            job.AppendLog("錯誤：" + ex.Message);
        }
        finally
        {
            job.CompletedAt = DateTime.Now;
        }
    }

    private void ClearFinishedJobs()
    {
        foreach (var job in Jobs.Where(j => j.IsFinished).ToList())
            Jobs.Remove(job);

        OnPropertyChanged(nameof(HasJobs));
        ClearFinishedJobsCommand.RaiseCanExecuteChanged();
    }

    public string BuildTargetName(RepositoryInfo repo)
    {
        var template = string.IsNullOrWhiteSpace(NameTemplate) ? "{name}" : NameTemplate;
        var name = template
            .Replace("{name}", repo.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{owner}", repo.Owner, StringComparison.OrdinalIgnoreCase)
            .Replace("{platform}", repo.SourcePlatform, StringComparison.OrdinalIgnoreCase);
        return Sanitize(name);
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "repository";
        var chars = name.Trim().Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-').ToArray();
        var cleaned = new string(chars).Trim('-', '.');
        while (cleaned.Contains("--", StringComparison.Ordinal))
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        if (cleaned.Length == 0) cleaned = "repository";
        return cleaned.Length > 100 ? cleaned[..100] : cleaned;
    }

    private void ResetAddForm()
    {
        NewToken = string.Empty;
        NewUsername = string.Empty;
        NewApiUrl = string.Empty;
        NewOrganization = string.Empty;
        NewProject = string.Empty;
        NewGitUser = string.Empty;
        NewGitPass = string.Empty;
        AddError = string.Empty;
        IsAdvancedOpen = false;
    }

    private void SetStatus(string message, bool isError)
    {
        StatusIsError = isError;
        StatusMessage = message;
    }

    private void OnRepositoriesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RaiseListStats();

    private void OnRepositoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RepositoryInfo.IsSelected))
            RaiseSelectionStats();
    }

    private void OnTargetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TargetOption.IsChecked)) return;
        OnPropertyChanged(nameof(CheckedTargetCount));
        OnPropertyChanged(nameof(CanStartMirror));
        OnPropertyChanged(nameof(StartButtonText));
        StartMirrorCommand.RaiseCanExecuteChanged();
    }

    private void RaiseListStats()
    {
        OnPropertyChanged(nameof(RepoCountText));
        OnPropertyChanged(nameof(TotalSizeText));
        OnPropertyChanged(nameof(HasRepositories));
        OnPropertyChanged(nameof(HasNoRepositories));
        RaiseSelectionStats();
    }

    private void RaiseSelectionStats()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(CanStartMirror));
        OnPropertyChanged(nameof(StartButtonText));
        OnPropertyChanged(nameof(NamePreview));
        StartMirrorCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>說明面板裡的一個步驟：編號 + 文字。</summary>
public sealed record SetupStep(int Number, string Text);
