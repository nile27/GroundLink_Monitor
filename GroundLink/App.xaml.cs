using GroundLink.Data;
using GroundLink.Services;
using GroundLink.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace GroundLink;

public partial class App : Application
{
    
    private const int SatelliteCount = 10;

    // 측정 결과를 출력 창 + 파일 둘 다에 기록
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "measure.log");

    internal static void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} {message}";
        Debug.WriteLine(line);
        File.AppendAllText(LogPath, line + Environment.NewLine);
    }

    private ServiceProvider? _provider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var sw = Stopwatch.StartNew();

        // ① DI 등록
        var services = new ServiceCollection();
        services.AddSingleton<ISatelliteService>(_ => new FakeSatelliteService(SatelliteCount));
        //services.AddSingleton<ITelemetryRepository, SqlTelemetryRepository>();
        services.AddSingleton<ITelemetryRepository>(_ =>
    new SqlTelemetryRepository(SqlTelemetryRepository.SaveMode.BulkCopy));
        services.AddSingleton<TelemetryRecorder>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        _provider = services.BuildServiceProvider();

        // ② 기록기 시작 ← 여기!
        _provider.GetRequiredService<TelemetryRecorder>();

        // ③ 창 만들기 + 측정
        var window = _provider.GetRequiredService<MainWindow>();

        window.ContentRendered += (_, _) =>
        {
            Log($"[측정] 위성 {SatelliteCount}대 / 첫 프레임: {sw.ElapsedMilliseconds} ms");

            window.Dispatcher.BeginInvoke(() =>
            {
                var mb = Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;
                var actual = _provider!.GetRequiredService<MainViewModel>().Satellites.Count;
                var cards = window.CountRealizedCards();
                Log($"[측정] 실제 {actual}대 / 만들어진 카드 {cards}장 / 화면 준비 완료: {sw.ElapsedMilliseconds} ms / 메모리: {mb} MB");
            }, DispatcherPriority.ApplicationIdle);
        };

        window.Show();
        StartUiLagProbe();
    }

    // [측정 2] UI 지연: 100ms 타이머가 실제로 얼마나 늦게 오는지 5초마다 최대값 출력
    private void StartUiLagProbe()
    {
        var probe = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        var last = Stopwatch.StartNew();
        long maxLag = 0;
        int count = 0;

        probe.Tick += (_, _) =>
        {
            long lag = last.ElapsedMilliseconds - 100;
            last.Restart();
            maxLag = Math.Max(maxLag, lag);

            if (++count % 50 == 0) // 100ms × 50 = 5초
            {
                Log($"[측정] 최근 5초 최대 UI 지연: {maxLag} ms");
                maxLag = 0;
            }
        };
        probe.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _provider?.GetService<ISatelliteService>()?.Stop();
        _provider?.Dispose();
        base.OnExit(e);
    }
}