using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using GroundLink.Services;
using GroundLink.ViewModels;

namespace GroundLink;

public partial class App : Application
{
    private ServiceProvider? _provider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton<ISatelliteService, FakeSatelliteService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _provider = services.BuildServiceProvider();
        _provider.GetRequiredService<MainWindow>().Show();
    }

    // 앱 종료 시 타이머 정지, 컨테이너 정리
    protected override void OnExit(ExitEventArgs e)
    {
        _provider?.GetService<ISatelliteService>()?.Stop();
        _provider?.Dispose();
        base.OnExit(e);
    }
}