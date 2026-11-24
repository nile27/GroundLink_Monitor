using System.Diagnostics;
using System.Windows;
using GroundLink.Charts;
using GroundLink.Data;
using GroundLink.ViewModels;
using System.Windows.Threading;
using GroundLink.Models;

namespace GroundLink;

public partial class MainWindow : Window
{
    // DB 접속 정보
    private const string ConnectionString = @"Server=localhost\SQLEXPRESS;Database=MonitorDB;Trusted_Connection=True;TrustServerCertificate=True;";
    private const string SatelliteId = "SAT-001";

    private readonly MainViewModel viewModel;
    
    // 실시간 배터리 선
    private readonly ScottPlot.Plottables.DataLogger batteryLiveLogger;

    // 실시간 온도 선
    private readonly ScottPlot.Plottables.DataLogger temperatureLiveLogger;

    // UI 멈춤 측정
    private readonly DispatcherTimer uiHeartbeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Stopwatch heartbeatClock = Stopwatch.StartNew();
    private double lastTickMs;
    private double maxGapMs;
    private double lastReportMs;

    public MainWindow()
    {
        InitializeComponent();

        // 차트 글꼴
        ChartDrawer.RegisterKoreanFont();

        // ViewModel 연결
        viewModel = new MainViewModel(
            new TelemetryRepository(ConnectionString), SatelliteId,
            action => Dispatcher.InvokeAsync(action));   // UI스레드 추가
        viewModel.HistoryLoaded += OnHistoryLoaded;
        viewModel.TelemetryReceived += OnTelemetryReceived;
        DataContext = viewModel;

        // 차트 준비
        batteryLiveLogger = ChartDrawer.SetupLive(BatteryLivePlot, 0, 100, 60);             // 실시간 배터리 차트
        temperatureLiveLogger = ChartDrawer.SetupLive(TemperatureLivePlot, -10, 50, 0, 40); // 실시간 온도 차트
        ChartDrawer.ApplyTheme(BatteryHistoryPlot);                                         // 빈 차트 색
        ChartDrawer.ApplyTheme(TemperatureHistoryPlot);                        // 빈 차트 색


        // 시작 화면: 어제 기록
        Loaded += (_, _) => viewModel.SelectPeriodCommand.Execute(MainViewModel.PeriodDay);

        // 측정: 그리기 시간, UI 멈춤
        BatteryHistoryPlot.Plot.RenderManager.RenderFinished += (_, render) =>
            Debug.WriteLine($"Render {render.Elapsed.TotalMilliseconds:F1} ms");
        uiHeartbeat.Tick += (_, _) => MeasureUiGap();
        uiHeartbeat.Start();

        // 종료 처리
        Closing += (_, _) => viewModel.Shutdown();
    }


    // 과거 기록 조회 끝 이벤트
    private void OnHistoryLoaded(HistoryChartData data)
    {
        ChartDrawer.DrawHistory(BatteryHistoryPlot, data.Battery, data.BucketSeconds, data.From, data.To, 0, 150, 60);
        ChartDrawer.DrawHistory(TemperatureHistoryPlot, data.Temperature, data.BucketSeconds, data.From, data.To, -10, 60, 0, 40);
    }

    // 수신 이벤트
    private void OnTelemetryReceived(Telemetry data)
    {
        ChartDrawer.AppendLive(BatteryLivePlot, batteryLiveLogger, data.RecordedAt, data.Battery);
        ChartDrawer.AppendLive(TemperatureLivePlot, temperatureLiveLogger, data.RecordedAt, data.Temperature);
    }

    // UI 멈춤 측정 함수
    private void MeasureUiGap()
    {
        double now = heartbeatClock.Elapsed.TotalMilliseconds;
        double gap = now - lastTickMs;      // 틱 간격
        lastTickMs = now;
        if (gap > maxGapMs)
            maxGapMs = gap;

        if (now - lastReportMs >= 1000)
        {
            Debug.WriteLine($"UI 최대 멈춤 {maxGapMs:F0} ms");
            maxGapMs = 0;
            lastReportMs = now;
        }
    }
}
