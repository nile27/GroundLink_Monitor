using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using GroundLink.Services;
using GroundLink.ViewModels;

namespace GroundLink; // ← 원래 파일의 네임스페이스 유지

public partial class App : Application
{
    // 앱 시작 시 실행
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // DI 컨테이너에 등록 (Nest 모듈의 providers와 같은 역할)
        var services = new ServiceCollection();
        services.AddSingleton<ISatelliteService, FakeSatelliteService>(); // ← 나중에 Api로 교체할 줄
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        var provider = services.BuildServiceProvider();

        // 컨테이너가 MainWindow → MainViewModel → FakeSatelliteService 순으로 알아서 만들어 넣어줌
        provider.GetRequiredService<MainWindow>().Show();
    }
}