using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GithubMirror.Services;
using GithubMirror.Views;

namespace GithubMirror;

public partial class App : Application
{
    /// <summary>
    /// 服務層接上後，在 Program.cs 設定這個屬性即可切換成真實實作。
    /// </summary>
    public static AppServices Services { get; set; } = AppServices.CreateSample();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(Services);

        base.OnFrameworkInitializationCompleted();
    }
}
