using System.Net;
using System.Text;
using GithubMirror.Models;
using GithubMirror.Services;
using GithubMirror.ViewModels;
using Xunit;

namespace GithubMirror.Tests;

public sealed class BackupWorkflowTests
{
    [Fact]
    public void CreateBackup_copies_accounts_and_settings_without_sharing_instances()
    {
        var vm = new MainWindowViewModel(AppServices.CreateSample());
        vm.Accounts.Add(new AccountCredential { Username = "alice", Token = "live-token" });
        vm.NameTemplate = "mirror-{name}";
        vm.KeepPrivate = false;
        var data = vm.CreateBackup(true, false, false, "https://example.invalid/song");
        Assert.Equal("alice", Assert.Single(data.Accounts).Username);
        Assert.Equal("live-token", data.Accounts[0].Token);
        Assert.NotSame(vm.Accounts[0], data.Accounts[0]);
        Assert.True(data.HideTutorial);
        Assert.Equal("mirror-{name}", data.NameTemplate);
        Assert.False(data.KeepPrivate);
        Assert.False(data.AutoPlayMusic);
        Assert.False(data.ContinuePlayback);
        Assert.Equal("https://example.invalid/song", data.MusicSourceUrl);
    }

    [Fact]
    public async Task Restore_replaces_accounts_clears_stale_selections_and_persists_preferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mirror-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var services = AppServices.CreateSample();
            var vm = new MainWindowViewModel(services);
            vm.Accounts.Add(new AccountCredential { Username = "old" });
            var data = new BackupData
            {
                Accounts = new() { new AccountCredential { Username = "restored", Token = "secret" } },
                HideTutorial = true, NameTemplate = "copy-{name}", KeepPrivate = false,
                AutoPlayMusic = false, ContinuePlayback = false, MusicSourceUrl = OpenMusicPlayer.Tracks[1].SourcePageUrl
            };
            var store = new AppPreferenceStore(directory);
            await vm.RestoreBackupAsync(data, store);
            Assert.Equal("restored", Assert.Single(vm.Accounts).Username);
            Assert.Equal("secret", Assert.Single(await services.Credentials.LoadAsync(default)).Token);
            Assert.NotSame(data.Accounts[0], vm.Accounts[0]);
            Assert.Empty(vm.Repositories);
            Assert.Equal(0, vm.CheckedTargetCount);
            Assert.False(vm.CanStartMirror);
            var persisted = store.Load();
            Assert.Equal("copy-{name}", persisted.NameTemplate);
            Assert.True(persisted.HideTutorial);
            Assert.False(persisted.AutoPlayMusic);
            Assert.False(persisted.ContinuePlayback);
            Assert.False(persisted.KeepPrivate);
            Assert.Equal(data.MusicSourceUrl, persisted.MusicSourceUrl);
            Assert.DoesNotContain("secret", File.ReadAllText(Path.Combine(directory, "preferences.json")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Failed_account_write_preserves_live_accounts_and_rolls_back_preferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mirror-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sample = AppServices.CreateSample();
            var vm = new MainWindowViewModel(new AppServices
            {
                Git = sample.Git, Mirror = sample.Mirror, Credentials = new FailingStore()
            });
            vm.Accounts.Add(new AccountCredential { Username = "original" });
            var store = new AppPreferenceStore(directory);
            store.Save(new AppPreferences { NameTemplate = "original-{name}" });
            await Assert.ThrowsAsync<IOException>(() => vm.RestoreBackupAsync(new BackupData(), store));
            Assert.Equal("original", Assert.Single(vm.Accounts).Username);
            Assert.Equal("original-{name}", store.Load().NameTemplate);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Restore_is_blocked_during_mirroring()
    {
        var vm = new MainWindowViewModel { IsMirroring = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RestoreBackupAsync(new BackupData(), new AppPreferenceStore()));
    }

    [Fact]
    public async Task Drive_listing_follows_pages_and_sends_session_authorization()
    {
        var requests = 0;
        using var client = new GoogleDriveBackupClient(new Handler(async request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("session", request.Headers.Authorization?.Parameter);
            var uri = request.RequestUri!.ToString();
            requests++;
            if (requests == 2) Assert.Contains("pageToken=next", uri);
            await Task.CompletedTask;
            return Json(requests == 1
                ? "{\"files\":[{\"id\":\"1\",\"name\":\"first.gmbak\"}],\"nextPageToken\":\"next\"}"
                : "{\"files\":[{\"id\":\"2\",\"name\":\"second.gmbak\"}]}");
        }));
        client.SetSession("session", 3600);
        Assert.Equal(2, (await client.ListAsync(default)).Count);
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task Upload_contains_encrypted_payload_without_plaintext_credentials()
    {
        var bytes = EncryptedBackup.Encrypt(new BackupData
        {
            Accounts = new() { new AccountCredential { Token = "never-send-this-in-clear" } }
        }, "1234");
        using var client = new GoogleDriveBackupClient(new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("uploadType=multipart", request.RequestUri!.Query);
            var content = await request.Content!.ReadAsByteArrayAsync();
            Assert.DoesNotContain("never-send-this-in-clear", Encoding.UTF8.GetString(content));
            Assert.True(content.AsSpan().IndexOf(bytes) >= 0);
            var text = Encoding.UTF8.GetString(content);
            Assert.Contains("githubMirrorBackup", text);
            Assert.Contains("\"parents\"", text);
            Assert.Contains("folder-githubmirror", text);
            return Json("{\"name\":\"saved.gmbak\"}");
        }));
        client.SetSession("session", 3600);
        client.SetBackupFolder("folder-githubmirror");
        Assert.Equal("saved.gmbak", await client.UploadAsync(bytes, default));
    }

    [Fact]
    public async Task Upload_creates_OAuth_GithubMirror_folder_when_missing()
    {
        var requests = new List<string>();
        using var client = new GoogleDriveBackupClient(new Handler(async request =>
        {
            var uri = request.RequestUri!.AbsoluteUri;
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
            requests.Add($"{request.Method.Method} {uri}");
            if (request.Method == HttpMethod.Get)
            {
                Assert.Contains("githubMirrorFolder", uri);
                return Json("{\"files\":[]}");
            }
            if (uri.Contains("uploadType=multipart", StringComparison.Ordinal))
            {
                Assert.Contains("app-folder-id", body);
                Assert.DoesNotContain("never-send-this-in-clear", body);
                return Json("{\"name\":\"saved.gmbak\"}");
            }
            Assert.Contains("application/vnd.google-apps.folder", body);
            Assert.Contains("githubMirrorFolder", body);
            if (body.Contains("\"OAuth\""))
            {
                Assert.DoesNotContain("parents", body);
                return Json("{\"id\":\"oauth-folder-id\",\"name\":\"OAuth\"}");
            }
            Assert.Contains("GithubMirror", body);
            Assert.Contains("oauth-folder-id", body);
            return Json("{\"id\":\"app-folder-id\",\"name\":\"GithubMirror\"}");
        }));
        client.SetSession("session", 3600);
        Assert.Equal("saved.gmbak", await client.UploadAsync(EncryptedBackup.Encrypt(new BackupData
        {
            Accounts = new() { new AccountCredential { Token = "never-send-this-in-clear" } }
        }, "1234"), default));
        Assert.Equal(5, requests.Count);
        Assert.Contains(requests, r => r.StartsWith("GET ", StringComparison.Ordinal) && r.Contains("oauth"));
        Assert.Contains(requests, r => r.StartsWith("POST ", StringComparison.Ordinal) && !r.Contains("uploadType"));
        Assert.Contains(requests, r => r.Contains("uploadType=multipart"));
    }

    [Fact]
    public async Task Upload_reuses_existing_OAuth_GithubMirror_folder()
    {
        var creates = 0;
        using var client = new GoogleDriveBackupClient(new Handler(async request =>
        {
            var uri = request.RequestUri!.AbsoluteUri;
            if (request.Method == HttpMethod.Get)
            {
                var q = Uri.UnescapeDataString(request.RequestUri.Query);
                if (q.Contains("value='oauth'")) return Json("{\"files\":[{\"id\":\"oauth-folder-id\",\"name\":\"OAuth\"}]}");
                Assert.Contains("oauth-folder-id", q);
                return Json("{\"files\":[{\"id\":\"app-folder-id\",\"name\":\"GithubMirror\"}]}");
            }
            if (uri.Contains("uploadType=multipart", StringComparison.Ordinal))
            {
                Assert.Contains("app-folder-id", await request.Content!.ReadAsStringAsync());
                return Json("{\"name\":\"saved.gmbak\"}");
            }
            creates++;
            return Json("{\"id\":\"unexpected\"}");
        }));
        client.SetSession("session", 3600);
        Assert.Equal("saved.gmbak", await client.UploadAsync(new byte[] { 1, 2, 3 }, default));
        Assert.Equal(0, creates);
        Assert.Equal("saved.gmbak", await client.UploadAsync(new byte[] { 4 }, default));
        Assert.Equal(0, creates);
    }

    [Fact]
    public async Task Unauthorized_response_expires_session_and_oversize_download_is_rejected()
    {
        using var unauthorized = new GoogleDriveBackupClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))));
        unauthorized.SetSession("session", 3600);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unauthorized.ListAsync(default));
        Assert.False(unauthorized.IsConnected);

        using var large = new GoogleDriveBackupClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[EncryptedBackup.MaximumFileSize + 1])
        })));
        large.SetSession("session", 3600);
        await Assert.ThrowsAsync<InvalidDataException>(() => large.DownloadAsync("id", default));
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
    private sealed class FailingStore : ICredentialStore
    {
        public Task<List<AccountCredential>> LoadAsync(CancellationToken ct) => Task.FromResult(new List<AccountCredential>());
        public Task SaveAsync(IEnumerable<AccountCredential> accounts, CancellationToken ct) => throw new IOException("disk full");
    }
}
