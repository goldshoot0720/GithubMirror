using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using GithubMirror.Services;

namespace GithubMirror.Views;

public partial class BackupWindow : Window
{
    private readonly GoogleDriveBackupClient _drive = new();
    private readonly Func<BackupData> _capture;
    private readonly Func<BackupData, Task> _restore;
    private CancellationTokenSource? _operation;
    private BackupData? _pending;
    private bool _committing;

    public BackupWindow() : this(() => new BackupData(), _ => Task.CompletedTask) { }
    public BackupWindow(Func<BackupData> capture, Func<BackupData, Task> restore)
    {
        _capture = capture;
        _restore = restore;
        InitializeComponent();
        Closing += (_, e) =>
        {
            if (_operation is null) return;
            e.Cancel = true;
            if (!_committing) _operation.Cancel();
            StatusText.Text = _committing ? "正在儲存還原資料，請稍候再關閉。" : "正在取消操作，完成後可關閉。";
        };
        Closed += (_, _) =>
        {
            _pending = null;
            ClearPasswords();
            _drive.Dispose();
        };
    }

    private static readonly IBrush StatusNormalText = new SolidColorBrush(Color.Parse("#1F2328"));
    private static readonly IBrush StatusErrorText = new SolidColorBrush(Color.Parse("#B42318"));
    private static readonly IBrush StatusNormalBack = new SolidColorBrush(Color.Parse("#F0F2F5"));
    private static readonly IBrush StatusErrorBack = new SolidColorBrush(Color.Parse("#FDECEE"));

    /// <summary>錯誤訊息用紅底紅字呈現，避免和一般進度訊息混在一起被忽略。</summary>
    private void SetStatus(string text, bool isError)
    {
        StatusText.Text = text;
        StatusText.Foreground = isError ? StatusErrorText : StatusNormalText;
        StatusBox.Background = isError ? StatusErrorBack : StatusNormalBack;
    }

    private async Task RunAsync(string status, Func<CancellationToken, Task> action)
    {
        if (_operation is not null) return;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        ActionsPanel.IsEnabled = false;
        BusyProgress.IsVisible = true;
        CancelButton.IsVisible = true;
        SetStatus(status, isError: false);
        try { await action(operation.Token); }
        catch (OperationCanceledException)
        {
            SetStatus("操作已取消或逾時。如正在上傳，請重新整理確認是否已建立備份。", isError: true);
        }
        catch (Exception ex)
        {
            SetStatus(ex switch
            {
                JsonException => "設定檔或備份內容不是支援的格式，請重新選擇檔案。",
                HttpRequestException => "無法連接 Google Drive，請檢查網路；若上傳中斷，重新整理清單確認結果。",
                UnauthorizedAccessException => "無法存取本機檔案，請檢查檔案權限。",
                IOException => ex is InvalidDataException ? ex.Message : "讀寫檔案失敗，請檢查磁碟空間與檔案權限。",
                ArgumentException => ex.Message,
                InvalidOperationException => ex.Message,
                _ => "操作失敗，請檢查設定檔、網路與本機儲存空間後重試。"
            }, isError: true);
        }
        finally
        {
            _operation = null;
            _committing = false;
            ActionsPanel.IsEnabled = true;
            BusyProgress.IsVisible = false;
            CancelButton.IsVisible = false;
            CancelButton.IsEnabled = true;
            UploadButton.IsEnabled = RefreshButton.IsEnabled = PreviewButton.IsEnabled = _drive.IsConnected;
            ConnectionText.Text = _drive.IsConnected ? "Google 已連接；關閉此視窗後登入狀態會清除。" : "尚未登入或登入已到期，請選擇 OAuth JSON 重新登入。";
        }
    }

    private async void SignIn_Click(object? sender, RoutedEventArgs e) => await RunAsync("請選擇 OAuth 設定檔，並在瀏覽器完成登入。", async ct =>
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "選擇 Google OAuth 桌面應用程式 JSON", AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
        });
        if (files.Count == 0) { SetStatus("未選擇設定檔。", isError: false); return; }
        ClearPreview();
        BackupFiles.ItemsSource = null;
        await using var input = await files[0].OpenReadAsync();
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > 64 * 1024) throw new InvalidDataException("OAuth 設定檔過大，請選擇下載的桌面用戶端 JSON。");
            output.Write(buffer, 0, count);
        }
        await _drive.SignInAsync(System.Text.Encoding.UTF8.GetString(output.ToArray()).TrimStart('\uFEFF'), ct);
        await RefreshFilesAsync(ct);
    });

    private async Task RefreshFilesAsync(CancellationToken ct)
    {
        ClearPreview();
        BackupFiles.ItemsSource = null;
        var files = await _drive.ListAsync(ct);
        BackupFiles.ItemsSource = files;
        BackupFiles.SelectedIndex = files.Count == 0 ? -1 : 0;
        StatusText.Text = files.Count == 0 ? "此帳號與 OAuth 應用程式尚無備份，可先建立新備份。" : $"找到 {files.Count} 份備份，請選擇要還原的檔案。";
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e) =>
        await RunAsync("正在讀取備份清單…", RefreshFilesAsync);

    private async void Upload_Click(object? sender, RoutedEventArgs e) => await RunAsync("正在加密並上傳備份…", async ct =>
    {
        var password = BackupPassword.Text ?? string.Empty;
        if (password != ConfirmPassword.Text) throw new ArgumentException("兩次輸入的備份密碼不一致。");
        var snapshot = _capture();
        byte[] encrypted;
        try { encrypted = await Task.Run(() => EncryptedBackup.Encrypt(snapshot, password), ct); }
        finally { BackupPassword.Text = ConfirmPassword.Text = string.Empty; }
        ct.ThrowIfCancellationRequested();
        var name = await _drive.UploadAsync(encrypted, ct);
        StatusText.Text = $"已上傳：{name}。";
        try
        {
            await RefreshFilesAsync(ct);
            StatusText.Text = $"備份完成：{name}";
        }
        catch (Exception)
        {
            StatusText.Text = $"備份已上傳：{name}；清單更新失敗，請稍後重新整理，無需再次上傳。";
        }
    });

    private async void Preview_Click(object? sender, RoutedEventArgs e) => await RunAsync("正在下載並驗證備份…", async ct =>
    {
        ClearPreview();
        if (BackupFiles.SelectedItem is not DriveBackupFile file) throw new InvalidOperationException("請先選擇備份檔案。");
        var password = RestorePassword.Text ?? string.Empty;
        BackupData data;
        try
        {
            var bytes = await _drive.DownloadAsync(file.Id, ct);
            data = await Task.Run(() => EncryptedBackup.Decrypt(bytes, password), ct);
        }
        finally { RestorePassword.Text = string.Empty; }
        ct.ThrowIfCancellationRequested();
        _pending = data;
        var platforms = string.Join("、", data.Accounts.GroupBy(a => a.Platform).Select(g => $"{g.Key} {g.Count()} 個"));
        PreviewText.Text = $"備份：{file.Name}\n建立時間：{data.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n" +
            $"將以 {data.Accounts.Count} 個備份帳號取代所有本機帳號（{platforms}）。\n" +
            $"命名規則：{data.NameTemplate}\n保留私有：{(data.KeepPrivate ? "是" : "否")}；自動播放：{(data.AutoPlayMusic ? "是" : "否")}；接續播放：{(data.ContinuePlayback ? "是" : "否")}；啟動教學：{(data.HideTutorial ? "隱藏" : "顯示")}；協作專案：{(data.IncludeCollaboratorRepos ? "顯示" : "隱藏")}；所有可能專案：{(data.IncludeAllAccessibleRepos ? "顯示" : "隱藏")}。\n" +
            "音樂選擇一併還原；不會修改遠端 Git 倉庫。確認後需重新整理專案並選擇鏡像目標。";
        PreviewPanel.IsVisible = true;
        StatusText.Text = "密碼與檔案驗證成功。請檢查摘要後確認還原，或關閉視窗保留現有資料。";
    });

    private async void Restore_Click(object? sender, RoutedEventArgs e) => await RunAsync("正在還原本機帳號與設定…", async ct =>
    {
        if (_pending is null) throw new InvalidOperationException("請先解密預覽備份。");
        ct.ThrowIfCancellationRequested();
        _committing = true;
        CancelButton.IsEnabled = false;
        await _restore(_pending);
        ClearPreview();
        ClearPasswords();
        StatusText.Text = "還原完成。關閉視窗後按主畫面的「重新整理」載入專案。";
    });

    private void BackupSelectionChanged(object? sender, SelectionChangedEventArgs e) => ClearPreview();
    private void ClearPreview()
    {
        _pending = null;
        if (PreviewPanel is not null) PreviewPanel.IsVisible = false;
    }
    private void ClearPasswords() => BackupPassword.Text = ConfirmPassword.Text = RestorePassword.Text = string.Empty;
    private void Cancel_Click(object? sender, RoutedEventArgs e) { if (!_committing) _operation?.Cancel(); }
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
    private void OpenCloud_Click(object? sender, RoutedEventArgs e)
    {
        if (!SystemBrowser.TryOpen("https://console.cloud.google.com/apis/credentials", out var error)) SetStatus(error, isError: true);
    }
}
