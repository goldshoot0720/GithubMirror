using GithubMirror.Services;
using Xunit;

namespace GithubMirror.Tests;

public class OpenMusicPlayerTests
{
    [Fact]
    public void Continue_playback_advances_and_wraps_the_playlist_by_default()
    {
        using var player = new OpenMusicPlayer(_ => Task.FromResult("https://invalid.example/audio.mp3"));
        Assert.True(player.ContinueToNextTrack);
        Assert.Equal(OpenMusicPlayer.Tracks[0], player.SelectedTrack);
        Assert.Equal(OpenMusicPlayer.Tracks[1], OpenMusicPlayer.TrackAfter(OpenMusicPlayer.Tracks[0]));
        Assert.Equal(OpenMusicPlayer.Tracks[0], OpenMusicPlayer.TrackAfter(OpenMusicPlayer.Tracks[^1]));
        player.AdvanceToNextTrack();
        Assert.Equal(OpenMusicPlayer.Tracks[1], player.SelectedTrack);
        player.SelectTrack(OpenMusicPlayer.Tracks[^1]);
        player.AdvanceToNextTrack();
        Assert.Equal(OpenMusicPlayer.Tracks[0], player.SelectedTrack);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stopping_or_closing_during_loading_does_not_start_playback(bool close)
    {
        if (!OperatingSystem.IsWindows()) return;
        var pendingUrl = new TaskCompletionSource<string>();
        using var player = new OpenMusicPlayer(_ => pendingUrl.Task);
        var play = player.PlayAsync();
        Assert.Equal(MusicPlaybackState.Loading, player.State);

        player.Stop();
        if (close) player.Dispose();
        pendingUrl.SetResult("https://invalid.example/audio.mp3");
        await play.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MusicPlaybackState.Stopped, player.State);
        Assert.Null(player.ErrorMessage);
    }

    [Fact]
    public async Task Changing_track_ignores_failure_from_previous_pending_track()
    {
        if (!OperatingSystem.IsWindows()) return;
        var pendingUrl = new TaskCompletionSource<string>();
        using var player = new OpenMusicPlayer(_ => pendingUrl.Task);
        var play = player.PlayAsync();
        player.SelectTrack(OpenMusicPlayer.Tracks[2]);
        pendingUrl.SetException(new InvalidOperationException("old track failed"));
        await play.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(OpenMusicPlayer.Tracks[2], player.SelectedTrack);
        Assert.Equal(MusicPlaybackState.Stopped, player.State);
        Assert.Null(player.ErrorMessage);
    }
}
