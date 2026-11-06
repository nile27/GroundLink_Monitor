using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using GroundLink.Data;
using GroundLink.Diagnostics;
using GroundLink.Services;
using GroundLink.ViewModels;

namespace GroundLink;

/// <summary>
/// 앱 시작점. DI 컨테이너에 부품을 등록하고 메인 창을 띄운다.
/// (App.xaml에는 StartupUri가 없어야 함. 창은 여기서 직접 띄운다)
/// </summary>
public partial class App : Application
{
    private const int SatelliteCount = 10; // 가짜 위성 수

    private ServiceProvider? _provider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        PerfLog.StartStartupTimer(); // 개발용 시작 시간 측정 (Release 빌드에선 자동으로 빠짐)

        // ① DI 등록 (Nest 모듈의 providers와 같은 역할)
        var services = new ServiceCollection();
        services.AddSingleton<ISatelliteService>(_ => new FakeSatelliteService(SatelliteCount)); // 나중에 서버 연결 버전으로 교체할 줄
        services.AddSingleton<ITelemetryRepository, SqlTelemetryRepository>();
        services.AddSingleton<TelemetryRecorder>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        _provider = services.BuildServiceProvider();

        // ② 기록기 시작
        //    싱글톤은 처음 꺼낼 때 만들어지는데, 기록기는 아무도 안 찾으므로 여기서 직접 꺼낸다
        _provider.GetRequiredService<TelemetryRecorder>();

        // ③ 메인 창 (MainWindow → MainViewModel → 서비스·저장소 순으로 컨테이너가 알아서 만듦)
        var window = _provider.GetRequiredService<MainWindow>();
        PerfLog.TrackStartup(window, SatelliteCount); // Show() 전에 걸어야 첫 프레임을 놓치지 않음
        window.Show();

        // ④ 개발용 UI 지연 측정 (Release 빌드에선 자동으로 빠짐)
        PerfLog.StartUiLagProbe();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 타이머 정지, 컨테이너 정리
        _provider?.GetService<ISatelliteService>()?.Stop();
        _provider?.Dispose();
        base.OnExit(e);
    }
}
