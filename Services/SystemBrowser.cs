using System;
using System.Diagnostics;

namespace GithubMirror.Services;

/// <summary>用系統預設瀏覽器開啟網址（教學面板的「開啟設定頁面」按鈕用）。</summary>
public static class SystemBrowser
{
    /// <summary>開啟網址；成功回傳 true，失敗把原因放進 <paramref name="error"/>。</summary>
    public static bool TryOpen(string? url, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            error = "沒有可開啟的網址。";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = $"網址格式不正確：{url}";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            error = $"無法開啟瀏覽器：{ex.Message}";
            return false;
        }
    }
}
