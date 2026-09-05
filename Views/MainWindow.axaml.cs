using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GithubMirror.Models;
using GithubMirror.Services;
using GithubMirror.ViewModels;

namespace GithubMirror.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly OpenMusicPlayer _musicPlayer = new();
    private bool _ownsAutomaticPlayback;
    private bool _tutorialOpened;
    private readonly AppPreferenceStore _preferences = new();
    private bool _preferencesLoaded;

    /// <summary>設計工具 / 預覽器用。</summary>
    public MainWindow() : this(AppServices.CreateSample()) { }

    public MainWindow(AppServices services)
    {
        _viewModel = new MainWindowViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        KeyDown += WindowKeyDown;
        ApplyResponsiveLayout();
        MusicTrackCombo.ItemsSource = OpenMusicPlayer.Tracks;
        MusicTrackCombo.SelectedIndex = 0;
        _viewModel.PropertyChanged += ViewModelPropertyChanged;
        _musicPlayer.StateChanged += MusicPlayerStateChanged;
        Closed += WindowClosed;
        UpdateMusicUi();
        try
        {
            ApplyPreferences(_preferences.Load());
            _preferencesLoaded = true;
        }
        catch (Exception)
        {
            _viewModel.StatusMessage = "無法讀取本機設定，已使用預設值。請檢查設定檔與權限。";
            _viewModel.StatusIsError = true;
        }
        Closing += (_, e) =>
        {
            if (!_preferencesLoaded) return;
            try { SavePreferences(); }
            catch (Exception)
            {
                _viewModel.StatusMessage = "本機設定儲存失敗，這次設定變更未保存。";
                _viewModel.StatusIsError = true;
            }
        };
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ApplyResponsiveLayout();
        await _viewModel.InitializeAsync();
        if (!_tutorialOpened)
        {
            _tutorialOpened = true;
            if (!TutorialPreferences.LoadHidden())
                await ShowTutorialAsync();
        }
    }

    private void WindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F || e.KeyModifiers != KeyModifiers.Control) return;
        ProjectSearchBox.Focus();
        ProjectSearchBox.SelectAll();
        e.Handled = true;
    }

    /// <summary>
    /// 視窗變窄時同步縮小左右側欄，確保中間的「專案」欄永遠有足夠寬度，
    /// 不會像先前那樣在 1366 寬的螢幕上被擠成 0 而整欄消失。
    /// </summary>
    private void ApplyResponsiveLayout()
    {
        if (MainLayoutGrid.ColumnDefinitions.Count < 3) return;

        var width = Bounds.Width > 0 ? Bounds.Width : Width;

        var (left, right) = width switch
        {
            >= 1600 => (288d, 348d),
            >= 1400 => (268d, 320d),
            _       => (244d, 296d)
        };

        MainLayoutGrid.ColumnDefinitions[0].Width = new GridLength(left);
        MainLayoutGrid.ColumnDefinitions[2].Width = new GridLength(right);
    }

    private void ApplyPreferences(AppPreferences preferences)
    {
        _viewModel.NameTemplate = preferences.NameTemplate;
        _viewModel.KeepPrivate = preferences.KeepPrivate;
        _viewModel.IncludeCollaboratorRepos = preferences.IncludeCollaboratorRepos;
        _viewModel.IncludeAllAccessibleRepos = preferences.IncludeAllAccessibleRepos;
        _viewModel.ShowAllCommitSizes = preferences.ShowAllCommitSizes;
        AutoPlayMusicCheck.IsChecked = preferences.AutoPlayMusic;
        ContinuePlaybackCheck.IsChecked = preferences.ContinuePlayback;
        _musicPlayer.ContinueToNextTrack = preferences.ContinuePlayback;
        var index = Array.FindIndex(OpenMusicPlayer.Tracks, t => t.SourcePageUrl == preferences.MusicSourceUrl);
        MusicTrackCombo.SelectedIndex = index < 0 ? 0 : index;
    }

    private void SavePreferences()
    {
        var preferences = _preferences.Load();
        preferences.NameTemplate = _viewModel.NameTemplate;
        preferences.KeepPrivate = _viewModel.KeepPrivate;
        preferences.IncludeCollaboratorRepos = _viewModel.IncludeCollaboratorRepos;
        preferences.IncludeAllAccessibleRepos = _viewModel.IncludeAllAccessibleRepos;
        preferences.ShowAllCommitSizes = _viewModel.ShowAllCommitSizes;
        preferences.AutoPlayMusic = AutoPlayMusicCheck.IsChecked == true;
        preferences.ContinuePlayback = ContinuePlaybackCheck.IsChecked == true;
        preferences.MusicSourceUrl = _musicPlayer.SelectedTrack.SourcePageUrl;
        _preferences.Save(preferences);
    }

    private async void ShowBackup_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsMirroring || _viewModel.IsLoading || _viewModel.IsVerifying)
        {
            _viewModel.StatusMessage = "請等待載入、帳號驗證或鏡像工作結束，再開啟設定檔備份。";
            return;
        }
        var window = new BackupWindow(
            () => _viewModel.CreateBackup(_preferences.Load().HideTutorial,
                AutoPlayMusicCheck.IsChecked == true, ContinuePlaybackCheck.IsChecked == true,
                _musicPlayer.SelectedTrack.SourcePageUrl),
            async data =>
            {
                await _viewModel.RestoreBackupAsync(data, _preferences);
                _musicPlayer.Stop();
                _ownsAutomaticPlayback = false;
                ApplyPreferences(_preferences.Load());
                _preferencesLoaded = true;
            });
        await window.ShowDialog(this);
    }

    private async void ShowTutorial_Click(object? sender, RoutedEventArgs e) => await ShowTutorialAsync();

    private Task ShowTutorialAsync() => new UserGuideWindow().ShowDialog(this);

    private async void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsMirroring)) return;

        if (_viewModel.IsMirroring)
        {
            if (AutoPlayMusicCheck.IsChecked == true && !_musicPlayer.IsPlaying)
            {
                _ownsAutomaticPlayback = true;
                await _musicPlayer.PlayAsync();
            }
        }
        else if (_ownsAutomaticPlayback)
        {
            _musicPlayer.Stop();
            _ownsAutomaticPlayback = false;
        }
    }

    /// <summary>專案名稱的連結：點一下開啟該專案在來源平台上的頁面。</summary>
    private void RepositoryLink_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not RepositoryInfo repository) return;

        _viewModel.OpenRepositoryCommand.Execute(repository);
        e.Handled = true;
    }

    /// <summary>
    /// 在專案列上點兩下也等於開啟專案頁面。已經自己會處理點擊的控制項
    /// （名稱連結、勾選框）要跳過，否則一次雙擊會開兩個分頁。
    /// </summary>
    private void RepositoryRow_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source) return;

        var ancestors = source.GetSelfAndVisualAncestors().TakeWhile(v => v is not DataGridRow).ToList();
        if (ancestors.Any(v => v is Button or CheckBox)) return;

        var row = source.GetSelfAndVisualAncestors().OfType<DataGridRow>().FirstOrDefault();
        if ((row?.DataContext ?? RepositoryGrid.SelectedItem) is not RepositoryInfo repository) return;

        _viewModel.OpenRepositoryCommand.Execute(repository);
        e.Handled = true;
    }

    private void ContinuePlaybackChanged(object? sender, RoutedEventArgs e)
    {
        _musicPlayer.ContinueToNextTrack = ContinuePlaybackCheck.IsChecked == true;
    }

    private async void MusicTrackChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (MusicTrackCombo.SelectedIndex < 0 || MusicTrackCombo.SelectedIndex >= OpenMusicPlayer.Tracks.Length) return;

        var selected = OpenMusicPlayer.Tracks[MusicTrackCombo.SelectedIndex];
        if (_musicPlayer.SelectedTrack == selected) return;
        var resume = _musicPlayer.IsPlaying;
        _musicPlayer.SelectTrack(selected);
        if (resume) await _musicPlayer.PlayAsync();
    }

    private async void MusicToggle_Click(object? sender, RoutedEventArgs e)
    {
        _ownsAutomaticPlayback = false;
        if (_musicPlayer.IsPlaying) _musicPlayer.Pause();
        else await _musicPlayer.PlayAsync();
    }

    private void OpenMusicSource_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_musicPlayer.SelectedTrack.SourcePageUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MusicStatusText.Text = $"無法開啟來源：{ex.Message}";
        }
    }

    private void MusicPlayerStateChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(UpdateMusicUi);

    private void UpdateMusicUi()
    {
        MusicToggleButton.IsEnabled = _musicPlayer.State != MusicPlaybackState.Loading;
        MusicTrackCombo.IsEnabled = _musicPlayer.State != MusicPlaybackState.Loading;
        MusicToggleText.Text = _musicPlayer.State switch
        {
            MusicPlaybackState.Playing => "Ⅱ 暫停",
            MusicPlaybackState.Loading => "載入中…",
            _ => "▶ 播放"
        };
        var trackIndex = Array.IndexOf(OpenMusicPlayer.Tracks, _musicPlayer.SelectedTrack);
        if (trackIndex >= 0 && MusicTrackCombo.SelectedIndex != trackIndex)
            MusicTrackCombo.SelectedIndex = trackIndex;

        MusicStatusText.Text = _musicPlayer.State switch
        {
            MusicPlaybackState.Playing => _musicPlayer.ContinueToNextTrack
                ? $"接續播放中 · {_musicPlayer.SelectedTrack.Title}"
                : $"單曲循環 · {_musicPlayer.SelectedTrack.Title}",
            MusicPlaybackState.Paused => "已暫停",
            MusicPlaybackState.Loading => "正在從 OpenMusic 載入…",
            MusicPlaybackState.Failed => _musicPlayer.ErrorMessage ?? "播放失敗",
            _ => "已待命"
        };
    }

    private void WindowClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModelPropertyChanged;
        _musicPlayer.StateChanged -= MusicPlayerStateChanged;
        _musicPlayer.Dispose();
    }
}
