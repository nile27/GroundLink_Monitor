using System.Diagnostics;
using System.Windows;
using GroundLink.Charts;
using GroundLink.Data;
using GroundLink.ViewModels;
using System.Windows.Threading;

namespace GroundLink;

public partial class MainWindow : Window
{
    // DB 접속 정보 (나중에 설정 파일로 옮김)
    private const string ConnectionString = @"Server=localhost\SQLEXPRESS;Database=MonitorDB;Trusted_Connection=True;TrustServerCertificate=True;";
    private const string SatelliteId = "SAT-001";

    private readonly MainViewModel viewModel;
     

    
    private readonly DispatcherTimer uiHeartbeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Stopwatch heartbeatClock = Stopwatch.StartNew();
    private double lastTickMs;
    private double maxGapMs;
    private double lastReportMs;

    public MainWindow()
    {
        InitializeComponent();

        ChartDrawer.RegisterKoreanFont();   // 차트 그리기 전에 1회

        viewModel = new MainViewModel(
    new TelemetryRepository(ConnectionString), SatelliteId,
    action => Dispatcher.InvokeAsync(action)); // UI스레드 추가
        viewModel.HistoryLoaded += OnHistoryLoaded;   
        DataContext = viewModel;                     
        ChartDrawer.DrawSampleLive(BatteryLivePlot, 0, 100, 60);   // 실시간 배터리 (가짜 데이터, 카드 12에서 교체)
        ChartDrawer.ApplyTheme(BatteryHistoryPlot);                 // 버튼 누르기 전 빈 차트도 어둡게

       
        Loaded += (_, _) => viewModel.SelectPeriodCommand.Execute(MainViewModel.PeriodDay);

        BatteryHistoryPlot.Plot.RenderManager.RenderFinished += (_, render) =>
            Debug.WriteLine($"Render {render.Elapsed.TotalMilliseconds:F1} ms");


        uiHeartbeat.Tick += (_, _) => MeasureUiGap();
        uiHeartbeat.Start();
    }


    // 과거 기록 조회 끝 이벤트
    private void OnHistoryLoaded(HistoryChartData data)
    {
        ChartDrawer.DrawHistory(BatteryHistoryPlot, data.Points, data.BucketSeconds, 0, 150, 60);   // 배터리: 0~150, 경고 60 %
    }

    // UI 멈춤 측정 함수
    private void MeasureUiGap()
    {
        double now = heartbeatClock.Elapsed.TotalMilliseconds;
        double gap = now - lastTickMs;      // 지난 틱 이후 지난 시간 (정상이면 약 50 ms)
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
