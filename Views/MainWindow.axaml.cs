using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GithubMirror.Services;
using GithubMirror.ViewModels;

namespace GithubMirror.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly OpenMusicPlayer _musicPlayer = new();
    private bool _ownsAutomaticPlayback;
    private bool _tutorialOpened;

    /// <summary>設計工具 / 預覽器用。</summary>
    public MainWindow() : this(AppServices.CreateSample()) { }

    public MainWindow(AppServices services)
    {
        _viewModel = new MainWindowViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();
        MusicTrackCombo.ItemsSource = OpenMusicPlayer.Tracks;
        MusicTrackCombo.SelectedIndex = 0;
        _viewModel.PropertyChanged += ViewModelPropertyChanged;
        _musicPlayer.StateChanged += MusicPlayerStateChanged;
        Closed += WindowClosed;
        UpdateMusicUi();
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        await _viewModel.InitializeAsync();
        if (!_tutorialOpened)
        {
            _tutorialOpened = true;
            await ShowTutorialAsync();
        }
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

    private async void MusicTrackChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (MusicTrackCombo.SelectedIndex < 0 || MusicTrackCombo.SelectedIndex >= OpenMusicPlayer.Tracks.Length) return;

        var resume = _musicPlayer.IsPlaying;
        _musicPlayer.SelectTrack(OpenMusicPlayer.Tracks[MusicTrackCombo.SelectedIndex]);
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
        MusicStatusText.Text = _musicPlayer.State switch
        {
            MusicPlaybackState.Playing => $"播放中 · {_musicPlayer.SelectedTrack.Title}",
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
