using System;
using Avalonia;
using GithubMirror.Services;

namespace GithubMirror;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // ------------------------------------------------------------------
        // 服務層接上後，把下面這行換成真實實作即可，UI 不需要任何改動：
        //
        //   App.Services = new AppServices
        //   {
        //       Git         = new RealGitServiceProvider(),
        //       Credentials = new EncryptedCredentialStore(),
        //       Mirror      = new GitMirrorRunner()
        //   };
        //
        // 目前使用離線示範資料，方便先確認 UI 流程。
        // ------------------------------------------------------------------
        App.Services = RealAppServices.Create();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia 設計工具會呼叫這個方法
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
