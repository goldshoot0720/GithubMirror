using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GithubMirror.Models;

namespace GithubMirror.Views;

public partial class MirrorDialog : Window
{
    private IReadOnlyList<RepositoryInfo> _repositories = [];

    public AccountCredential? TargetAccount { get; private set; }
    public string? TargetRepositoryName { get; private set; }
    public bool IncludeLfs { get; private set; } = true;

    public MirrorDialog()
    {
        InitializeComponent();
    }

    public MirrorDialog(ObservableCollection<AccountCredential> accounts, IReadOnlyList<RepositoryInfo> repositories) : this()
    {
        _repositories = repositories;
        TargetAccountCombo.ItemsSource = accounts;
        RepositoryCountText.Text = repositories.Count == 1 ? "1 個來源倉庫" : $"{repositories.Count} 個來源倉庫";
        RepositorySummaryText.Text = string.Join("、", repositories.Take(4).Select(x => x.FullName)) + (repositories.Count > 4 ? "…" : string.Empty);
        RepositoryNamePanel.IsVisible = repositories.Count == 1;
        if (repositories.Count == 1) TargetRepositoryBox.Text = repositories[0].Name;

        var sourceId = repositories.FirstOrDefault()?.SourceAccount?.Id;
        TargetAccountCombo.SelectedItem = accounts.FirstOrDefault(x => x.Id != sourceId) ?? accounts.FirstOrDefault();
        if (accounts.Count == 0) ValidationText.Text = "尚未建立目的地連線，請先回主畫面新增。";
    }

    private void TargetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TargetAccountCombo.SelectedItem is not AccountCredential account)
        {
            TargetHelpText.Text = string.Empty;
            return;
        }
        TargetHelpText.Text = account.Platform switch
        {
            "AWS CodeCommit" => $"將使用 AWS CLI Profile「{(string.IsNullOrWhiteSpace(account.Profile) ? "default" : account.Profile)}」於 {account.Region} 建立倉庫。",
            "Azure Repos" => $"將建立於 {account.Organization} / {account.Project}。",
            "Bitbucket" => $"將建立於 Workspace「{(string.IsNullOrWhiteSpace(account.Organization) ? account.Username : account.Organization)}」。",
            _ => string.IsNullOrWhiteSpace(account.Organization)
                ? $"將建立於 {account.DisplayName} 的個人空間。"
                : $"將建立於「{account.Organization}」。"
        };
    }

    private void Mirror_Click(object? sender, RoutedEventArgs e)
    {
        TargetAccount = TargetAccountCombo.SelectedItem as AccountCredential;
        TargetRepositoryName = _repositories.Count == 1 ? TargetRepositoryBox.Text?.Trim() : null;
        IncludeLfs = IncludeLfsCheck.IsChecked == true;

        if (TargetAccount is null)
        {
            ValidationText.Text = "請選擇鏡像目的地。";
            return;
        }
        if (_repositories.Count == 1 && string.IsNullOrWhiteSpace(TargetRepositoryName))
        {
            ValidationText.Text = "請輸入目標倉庫名稱。";
            return;
        }
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
