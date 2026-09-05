using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GithubMirror.Services;

namespace GithubMirror.Views;

public partial class UserGuideWindow : Window
{
    private static readonly TutorialStep[] Steps =
    [
        new(
            "1",
            "先認識操作流程",
            "帳號 → 專案 → 目的地 → 音樂 → 開始鏡像",
            "GithubMirror 會讀取來源帳號可存取的 Git 專案，建立或確認目標倉庫，再完整複製所有分支、標籤與 refs。下方任務區會持續顯示目前步驟與進度。",
            "第一次操作可先用一個小型測試倉庫確認權限與命名規則。"),
        new(
            "2",
            "取得 Token 並加入帳號",
            "Token 欄位旁的「?」會顯示該平台的逐步取得方法",
            "以 GitHub 為例：頭像 → Settings → Developer settings → Personal access tokens → Tokens (classic) → "
            + "Generate new token (classic)，勾選 repo（要建到組織再加 write:org），產生後複製 ghp_ 開頭的字串，"
            + "貼回左側「新增帳號」的 Token 欄並按「驗證並加入」。GitLab、Bitbucket、Codeberg、Gitea、AWS CodeCommit、"
            + "Azure Repos 的步驟，切換平台後按「?」就會換成對應說明，也能直接按按鈕開啟該平台的設定頁面。",
            "Token 請只授予必要的 repository 讀寫與建立權限，不要把 Token 貼到專案名稱或搜尋欄。"),
        new(
            "3",
            "挑選要複製的專案",
            "搜尋、篩選，然後勾選一個或多個來源專案",
            "中間清單預設只顯示此帳號自己的專案（例如 goldshoot0720 只列出 goldshoot0720/…）。可勾選「協作的專案」看被邀請的倉庫，或「所有可能專案」看組織／群組等 Token 能存取的全部。大小欄預設是最近一次提交（目前檔案）；勾選「所有提交的大小」才改為全部 Git 歷史（含已刪檔，例如 1.2 GB 對上 10 MB）。清單上方搜尋列可依名稱、擁有者或語言過濾（Ctrl+F），並可切換 Public／Private。",
            "若剛加入帳號卻看不到專案，請按右上角「重新整理」並檢查 Token 的讀取權限。"),
        new(
            "4",
            "設定鏡像目的地",
            "右側可複選目標帳號並調整倉庫名稱",
            "勾選一個或多個目標帳號。名稱規則支援 {name}、{owner}、{platform}，例如 mirror-{name}。若啟用「私有專案維持私有」，來源為 Private 時目標也會建立為 Private。",
            "不要把同一個倉庫同時當成來源與目標；正式執行前請查看名稱預覽。"),
        new(
            "5",
            "選擇鏡像背景音樂",
            "鏡像期間可自動播放，也能隨時手動控制",
            "在右側「鏡像背景音樂」的下拉選單挑選十一首 OpenMusic 歌曲之一：〈鋒兄的傳奇人生〉、〈水電進化論〉、〈鋒塗力一起拚〉、〈排列組合的對話〉、〈結婚理由〉、〈水電王子〉、〈招財喵布布送祝福〉、〈集中統一領導〉、〈鋒兄進化論〉、〈塗神水電王子〉、〈喵布布本喵掉的毛〉。預設「接續播放」會在播完後接下一首，清單結束再從頭。保留「鏡像時自動播放」後，開始鏡像會播放、工作結束會停止；複製期間也可切換歌曲，或用播放／暫停按鈕手動控制。",
            "按「歌曲來源」可開啟 OpenMusic 原始頁面；音樂載入失敗不會中斷 Git 鏡像。"),
        new(
            "6",
            "開始並檢查結果",
            "按「開始鏡像」，從底部任務區追蹤進度",
            "鏡像進行中可按「停止鏡像」取消。完成後請到目標平台確認預設分支、所有分支與標籤；若失敗，將游標移到任務上查看紀錄，再依訊息修正 Token、保護分支或網路設定。",
            "push --mirror 會強制同步 refs，可能覆寫或刪除目標端同名 refs。重要目標倉庫請先備份。"),
        new(
            "7",
            "備份設定檔到 Google 雲端硬碟",
            "主畫面右上角與右側都有專屬「備份設定檔」按鈕",
            "按「備份設定檔」或右側「備份設定檔至 Google 雲端硬碟」，可把帳號 Token、鏡像名稱規則、音樂與教學偏好加密上傳到你的 Google 雲端硬碟「OAuth / GithubMirror」資料夾。換電腦時用同一組 OAuth 設定檔與備份密碼即可還原；不會上傳 Git 倉庫內容。",
            "首次使用需在 Google Cloud 啟用 Drive API，建立「桌面應用程式」OAuth 用戶端並下載 JSON。備份密碼為四位數字，忘記就無法還原。")
    ];

    private int _stepIndex;
    private readonly bool _initiallyHidden;

    public UserGuideWindow()
    {
        InitializeComponent();
        _initiallyHidden = TutorialPreferences.LoadHidden();
        DoNotShowAgainCheck.IsChecked = _initiallyHidden;
        Closing += SavePreferenceOnClosing;
        ShowStep();
    }

    private void SavePreferenceOnClosing(object? sender, WindowClosingEventArgs e)
    {
        var hidden = DoNotShowAgainCheck.IsChecked == true;
        if (hidden == _initiallyHidden) return;
        try
        {
            TutorialPreferences.SaveHidden(hidden);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            e.Cancel = true;
            PreferenceErrorText.Text = "無法儲存設定，請重試或還原勾選狀態後關閉。";
            PreferenceErrorText.IsVisible = true;
        }
    }

    private void Previous_Click(object? sender, RoutedEventArgs e)
    {
        if (_stepIndex <= 0) return;
        _stepIndex--;
        ShowStep();
    }

    private void Next_Click(object? sender, RoutedEventArgs e)
    {
        if (_stepIndex == Steps.Length - 1)
        {
            Close();
            return;
        }

        _stepIndex++;
        ShowStep();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void ShowStep()
    {
        var step = Steps[_stepIndex];
        StepCounterText.Text = $"{_stepIndex + 1} / {Steps.Length}";
        StepProgress.Value = _stepIndex + 1;
        StepGlyphText.Text = step.Glyph;
        StepTitleText.Text = step.Title;
        StepSummaryText.Text = step.Summary;
        StepBodyText.Text = step.Body;
        TipText.Text = step.Tip;
        PreviousButton.IsEnabled = _stepIndex > 0;
        NextButton.Content = _stepIndex == Steps.Length - 1 ? "開始使用" : "下一步";
    }

    private sealed record TutorialStep(string Glyph, string Title, string Summary, string Body, string Tip);
}
