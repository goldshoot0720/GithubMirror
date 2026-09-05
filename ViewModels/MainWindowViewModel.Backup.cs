using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GithubMirror.Services;

namespace GithubMirror.ViewModels;

public sealed partial class MainWindowViewModel
{
    public BackupData CreateBackup(bool hideTutorial, bool autoPlayMusic, bool continuePlayback, string musicSourceUrl) => new()
    {
        Accounts = Accounts.Select(a => a.Clone()).ToList(),
        HideTutorial = hideTutorial,
        NameTemplate = NameTemplate,
        KeepPrivate = KeepPrivate,
        AutoPlayMusic = autoPlayMusic,
        ContinuePlayback = continuePlayback,
        MusicSourceUrl = musicSourceUrl,
        IncludeCollaboratorRepos = IncludeCollaboratorRepos,
        IncludeAllAccessibleRepos = IncludeAllAccessibleRepos,
        ShowAllCommitSizes = ShowAllCommitSizes
    };

    public async Task RestoreBackupAsync(BackupData data, AppPreferenceStore preferences)
    {
        if (IsMirroring || IsLoading || IsVerifying)
            throw new InvalidOperationException("請等待帳號載入、驗證或鏡像工作結束後再還原。");
        EncryptedBackup.Validate(data);
        var accounts = data.Accounts.Select(a => a.Clone()).ToList();
        var previous = preferences.Load();
        preferences.Save(new AppPreferences
        {
            HideTutorial = data.HideTutorial, NameTemplate = data.NameTemplate,
            KeepPrivate = data.KeepPrivate, AutoPlayMusic = data.AutoPlayMusic,
            ContinuePlayback = data.ContinuePlayback,
            MusicSourceUrl = data.MusicSourceUrl,
            IncludeCollaboratorRepos = data.IncludeCollaboratorRepos,
            IncludeAllAccessibleRepos = data.IncludeAllAccessibleRepos,
            ShowAllCommitSizes = data.ShowAllCommitSizes
        });
        try
        {
            // Propagate failures: SaveAccountsAsync intentionally swallows errors for the normal UI.
            await _services.Credentials.SaveAsync(accounts, CancellationToken.None);
        }
        catch (Exception saveError)
        {
            try { preferences.Save(previous); }
            catch (Exception rollbackError)
            {
                throw new InvalidOperationException("帳號儲存失敗，且無法回復本機設定；請檢查磁碟與權限後重新還原。",
                    new AggregateException(saveError, rollbackError));
            }
            throw;
        }

        foreach (var repo in _allRepositories) repo.PropertyChanged -= OnRepositoryPropertyChanged;
        _allRepositories.Clear();
        foreach (var target in Targets) target.PropertyChanged -= OnTargetChanged;
        Accounts.Clear();
        foreach (var account in accounts) Accounts.Add(account);
        NameTemplate = data.NameTemplate;
        KeepPrivate = data.KeepPrivate;
        IncludeCollaboratorRepos = data.IncludeCollaboratorRepos;
        IncludeAllAccessibleRepos = data.IncludeAllAccessibleRepos;
        ShowAllCommitSizes = data.ShowAllCommitSizes;
        SearchText = string.Empty;
        VisibilityFilter = VisibilityFilters[0];
        Jobs.Clear();
        RebuildAccountViews();
        ApplyFilter();
        StartMirrorCommand.RaiseCanExecuteChanged();
        ClearFinishedJobsCommand.RaiseCanExecuteChanged();
        IsAddPanelOpen = Accounts.Count == 0;
        SetStatus($"已還原 {Accounts.Count} 個帳號與設定。按「重新整理」載入專案，再重新選擇鏡像目標。", false);
    }
}
