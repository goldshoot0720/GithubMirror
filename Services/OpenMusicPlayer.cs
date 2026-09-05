using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace GithubMirror.Services;

public sealed record OpenMusicTrack(string Title, string Artist, string SourcePageUrl, string FallbackAudioUrl)
{
    public string DisplayName => $"{Title} · {Artist}";
}

public enum MusicPlaybackState
{
    Stopped,
    Loading,
    Playing,
    Paused,
    Failed
}

public sealed partial class OpenMusicPlayer : IDisposable
{
    public static readonly OpenMusicTrack[] Tracks =
    [
        new(
            "鋒兄的傳奇人生",
            "黃馨鋒",
            "https://www.openmusic.ai/tw/song/7QFM8xkYlL",
            "https://cdn-media.openmusic.ai/openmusic/ugc/songs/1788544353204-35ptvr-400f323e2b57d62e.mp3"),
        new(
            "水電進化論",
            "黃馨鋒",
            "https://www.openmusic.ai/tw/song/JwYHPsrRr7",
            "https://cdn-media.openmusic.ai/openmusic/ugc/songs/1788544333098-26mgig-b6d582b2c05fdbc0.mp3"),
        new(
            "鋒塗力一起拚",
            "黃阿不點",
            "https://www.openmusic.ai/tw/song/p11bGIsK7Q",
            "https://cdn-media.openmusic.ai/openmusic/ugc/songs/1788544383306-6dlwj2-9e880c1bf08f16e5.mp3"),
        new(
            "排列組合的對話",
            "馮思敏",
            "https://www.openmusic.ai/tw/song/ocAHNKqKvZ",
            "https://cdn-media.openmusic.ai/openmusic/ugc/songs/1788550085354-asvrxa-650a77ea189075d3.mp3"),
        new(
            "結婚理由",
            "迪華敕擩",
            "https://www.openmusic.ai/tw/song/NHc6u5KDVD",
            "https://cdn-media.openmusic.ai/openmusic/ugc/songs/1788545437416-8npyyk-cb27d5895edaae49.mp3"),
        new(
            "水電王子",
            "鋒兄塗哥公關資訊鋒兄AI工作室塗哥建設",
            "https://www.openmusic.ai/tw/song/4HqKPWjhmN",
            "https://cdn-media.openmusic.ai/openmusic/ugc/songs/1788544569486-tq3zxv-e385d231021f4ce4.mp3"),
        new(
            "招財喵布布送祝福",
            "鋒兄塗哥公關資訊鋒兄AI工作室塗哥建設",
            "https://www.openmusic.ai/tw/song/ygPzygIV00",
            "https://cdn-media.openmusic.ai/openmusic/ugc/songs/1788544572151-1xe64q-e38c5d3879a5a2a8.mp3"),
        new(
            "集中統一領導",
            "feng feng",
            "https://www.openmusic.ai/tw/song/5M5cPaj54t",
            "https://cdn-media.openmusic.ai/openmusic/ugc/songs/1788544499770-7v76wu-dc44f1fe9c7d6e86.mp3"),
        new(
            "鋒兄進化論",
            "feng feng",
            "https://www.openmusic.ai/tw/song/0l7SVUmMPR",
            "https://cdn-media.openmusic.ai/openmusic/ugc/songs/1788544482329-umlbvm-d3cb47faefec5f00.mp3"),
        new(
            "塗神水電王子",
            "Hsin Feng Huang",
            "https://www.openmusic.ai/tw/song/t0mtRi7uEZ",
            "https://cdn-media.openmusic.ai/openmusic/ugc/songs/1788544570634-3xq6vk-eebf1cba15d498d0.mp3"),
        new(
            "喵布布本喵掉的毛",
            "Hsin Feng Huang",
            "https://www.openmusic.ai/tw/song/sjMdsmLlAg",
            "https://cdn-media.openmusic.ai/openmusic/ugc/songs/1788544480124-6pvmqm-5da74ddb4f535dba.mp3")
    ];

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<CancellationToken, Task<string>> _resolveAudioUrl;
    private WaveOutEvent? _output;
    private MediaFoundationReader? _reader;
    private MusicPlaybackState _state = MusicPlaybackState.Stopped;
    private bool _loop;
    private bool _disposed;
    private int _playbackVersion;

    public event EventHandler? StateChanged;

    public MusicPlaybackState State => _state;
    public string? ErrorMessage { get; private set; }
    public bool IsPlaying => State == MusicPlaybackState.Playing;
    public bool ContinueToNextTrack { get; set; } = true;
    public OpenMusicTrack SelectedTrack { get; private set; } = Tracks[0];

    public OpenMusicPlayer() => _resolveAudioUrl = ResolveAudioUrlAsync;

    internal OpenMusicPlayer(Func<CancellationToken, Task<string>> resolveAudioUrl) =>
        _resolveAudioUrl = resolveAudioUrl;

    public void SelectTrack(OpenMusicTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (SelectedTrack == track) return;
        Stop();
        SelectedTrack = track;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task PlayAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var version = _playbackVersion;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed || version != _playbackVersion) return;
            if (_state == MusicPlaybackState.Playing) return;

            if (!OperatingSystem.IsWindows())
            {
                SetFailure("內建音樂播放目前支援 Windows；請按「歌曲來源」在瀏覽器播放。");
                return;
            }

            if (_state == MusicPlaybackState.Paused && _output is not null)
            {
                _loop = true;
                _output.Play();
                SetState(MusicPlaybackState.Playing);
                return;
            }

            SetState(MusicPlaybackState.Loading);
            ErrorMessage = null;
            DisposePlaybackObjects();

            try
            {
                var audioUrl = await _resolveAudioUrl(cancellationToken);
                if (_disposed || version != _playbackVersion) return;
                var reader = await Task.Run(() => new MediaFoundationReader(audioUrl), cancellationToken);
                if (_disposed || version != _playbackVersion || cancellationToken.IsCancellationRequested)
                {
                    reader.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                    return;
                }
                _reader = reader;
                var output = new WaveOutEvent { DesiredLatency = 180, Volume = 0.65f };

                _output = output;
                _output.PlaybackStopped += PlaybackStopped;
                _output.Init(_reader);
                _loop = true;
                _output.Play();
                SetState(MusicPlaybackState.Playing);
            }
            catch (OperationCanceledException)
            {
                if (_disposed || version != _playbackVersion) return;
                DisposePlaybackObjects();
                SetState(MusicPlaybackState.Stopped);
                throw;
            }
            catch (Exception ex)
            {
                if (_disposed || version != _playbackVersion) return;
                DisposePlaybackObjects();
                SetFailure($"無法播放音樂：{ex.Message}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Pause()
    {
        if (!OperatingSystem.IsWindows() || _disposed || _output is null || _state != MusicPlaybackState.Playing) return;
        _loop = false;
        _output.Pause();
        SetState(MusicPlaybackState.Paused);
    }

    public void Stop()
    {
        if (_disposed) return;
        _playbackVersion++;
        _loop = false;
        DisposePlaybackObjects();

        ErrorMessage = null;
        SetState(MusicPlaybackState.Stopped);
    }

    private async Task<string> ResolveAudioUrlAsync(CancellationToken cancellationToken)
    {
        try
        {
            var html = await HttpClient.GetStringAsync(SelectedTrack.SourcePageUrl, cancellationToken);
            var match = OpenGraphAudioRegex().Match(html);
            if (!match.Success) match = AudioUrlJsonRegex().Match(html);
            if (match.Success && Uri.TryCreate(WebUtility.HtmlDecode(match.Groups["url"].Value), UriKind.Absolute, out var audioUri))
                return audioUri.AbsoluteUri;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The verified CDN URL remains available if the share page cannot be parsed.
        }

        return SelectedTrack.FallbackAudioUrl;
    }

    private void PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (!OperatingSystem.IsWindows() || _disposed || sender != _output) return;

        if (e.Exception is not null)
        {
            SetFailure($"音樂播放中斷：{e.Exception.Message}");
            return;
        }

        if (_loop)
        {
            if (ContinueToNextTrack)
            {
                if (_output is not null) _output.PlaybackStopped -= PlaybackStopped;
                AdvanceToNextTrack();
                SetState(MusicPlaybackState.Loading);
                var version = _playbackVersion;
                _ = PlayNextAfterStopAsync(version);
                return;
            }

            if (_reader?.CanSeek == true && _output is not null)
            {
                try
                {
                    _reader.Position = 0;
                    _output.Play();
                    return;
                }
                catch (Exception ex)
                {
                    SetFailure($"無法重新播放音樂：{ex.Message}");
                    return;
                }
            }
        }

        if (_state != MusicPlaybackState.Paused) SetState(MusicPlaybackState.Stopped);
    }

    internal static OpenMusicTrack TrackAfter(OpenMusicTrack current)
    {
        var index = Array.IndexOf(Tracks, current);
        return Tracks[index < 0 ? 0 : (index + 1) % Tracks.Length];
    }

    internal void AdvanceToNextTrack() => SelectedTrack = TrackAfter(SelectedTrack);

    private async Task PlayNextAfterStopAsync(int version)
    {
        try
        {
            await Task.Yield();
            if (_disposed || version != _playbackVersion) return;
            await PlayAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // PlayAsync reports failures; stop/cancel during the hand-off is ignored.
        }
    }

    private void SetFailure(string message)
    {
        _loop = false;
        ErrorMessage = message;
        SetState(MusicPlaybackState.Failed);
    }

    private void SetState(MusicPlaybackState state)
    {
        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DisposePlaybackObjects()
    {
        if (OperatingSystem.IsWindows())
        {
            if (_output is not null) _output.PlaybackStopped -= PlaybackStopped;
            _output?.Dispose();
        }
        _reader?.Dispose();
        _output = null;
        _reader = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _playbackVersion++;
        _loop = false;
        DisposePlaybackObjects();
        // An in-flight PlayAsync still needs to release the semaphore.
    }

    [GeneratedRegex("<meta\\s+property=[\\\"']og:audio[\\\"']\\s+content=[\\\"'](?<url>[^\\\"']+)", RegexOptions.IgnoreCase)]
    private static partial Regex OpenGraphAudioRegex();

    [GeneratedRegex("\\\"audioUrl\\\"\\s*:\\s*\\\"(?<url>https:[^\\\"]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AudioUrlJsonRegex();
}
