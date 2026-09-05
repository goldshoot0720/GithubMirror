using Avalonia.Controls;
using Avalonia.Interactivity;

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
            "中間清單可依名稱、擁有者或語言搜尋，也能切換 Public／Private 篩選。勾選專案後，上方會顯示已選數量；可用全選與清除快速調整。",
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
            "在右側「鏡像背景音樂」選擇〈最瞎結婚理由〉、〈排列組合的對話〉或〈最瞎結婚理由（版本二）〉。保留「鏡像時自動播放」後，開始鏡像會循環播放、工作結束會停止；複製期間也可切換歌曲，或用播放／暫停按鈕手動控制。",
            "按「歌曲來源」可開啟 OpenMusic 原始頁面；音樂載入失敗不會中斷 Git 鏡像。"),
        new(
            "6",
            "開始並檢查結果",
            "按「開始鏡像」，從底部任務區追蹤進度",
            "鏡像進行中可按「停止鏡像」取消。完成後請到目標平台確認預設分支、所有分支與標籤；若失敗，將游標移到任務上查看紀錄，再依訊息修正 Token、保護分支或網路設定。",
            "push --mirror 會強制同步 refs，可能覆寫或刪除目標端同名 refs。重要目標倉庫請先備份。")
    ];

    private int _stepIndex;

    public UserGuideWindow()
    {
        InitializeComponent();
        ShowStep();
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
