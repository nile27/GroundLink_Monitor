using System.Diagnostics;
using System.Windows;
using GroundLink.Data;
using GroundLink.Models;
using GroundLink.ViewModels;
using System.Windows.Threading;

namespace GroundLink;

public partial class MainWindow : Window
{
    // DB 접속 정보 (나중에 설정 파일로 옮김)
    private const string ConnectionString = @"Server=localhost\SQLEXPRESS;Database=MonitorDB;Trusted_Connection=True;TrustServerCertificate=True;";
    private const string SatelliteId = "SAT-001";

    private const string KoreanFont = "KoreanFont";   // 차트 한글 글꼴 이름 

    private readonly MainViewModel viewModel;
     

    
    private readonly DispatcherTimer uiHeartbeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Stopwatch heartbeatClock = Stopwatch.StartNew();
    private double lastTickMs;
    private double maxGapMs;
    private double lastReportMs;

    public MainWindow()
    {
        InitializeComponent();

        ScottPlot.Fonts.AddFontFile(KoreanFont, @"C:\Windows\Fonts\malgun.ttf");

        viewModel = new MainViewModel(
    new TelemetryRepository(ConnectionString), SatelliteId,
    action => Dispatcher.InvokeAsync(action)); // UI스레드 추가
        viewModel.HistoryLoaded += OnHistoryLoaded;   
        DataContext = viewModel;                     
        DrawSampleChart();
        StylePlot(BatteryHistoryPlot);   

       
        Loaded += (_, _) => viewModel.SelectPeriodCommand.Execute(MainViewModel.PeriodDay);

        BatteryHistoryPlot.Plot.RenderManager.RenderFinished += (_, render) =>
            Debug.WriteLine($"Render {render.Elapsed.TotalMilliseconds:F1} ms");


        uiHeartbeat.Tick += (_, _) => MeasureUiGap();
        uiHeartbeat.Start();
    }


    private void DrawSampleChart()
    {
        int count = 60;
        double[] xs = new double[count];
        double[] ys = new double[count];

        DateTime start = DateTime.Now.AddHours(-1);
        for (int i = 0; i < count; i++)
        {
            xs[i] = start.AddMinutes(i).ToOADate();        // 시각 → 숫자 (차트 x축은 숫자)
            ys[i] = 90 + 8 * Math.Sin(i / 10.0);           // 82~98 % 사이 파도 (가짜 값)
        }

        var liveLine = BatteryLivePlot.Plot.Add.Scatter(xs, ys);
        liveLine.Color = ScottPlot.Color.FromHex(Theme.Accent);
        BatteryLivePlot.Plot.Axes.DateTimeTicksBottom();   // x축 눈금을 날짜·시각으로
        StylePlot(BatteryLivePlot);                         // 축을 새로 만든 뒤에 색·글꼴
        var warningLine = BatteryLivePlot.Plot.Add.HorizontalLine(60);   // 경고 기준 60 %
        warningLine.LinePattern = ScottPlot.LinePattern.Dashed;
        warningLine.Color = ScottPlot.Color.FromHex(Theme.Caution);

        BatteryLivePlot.Plot.Axes.SetLimitsY(0, 100);      // 배터리 % → 0~100 고정 (0이 맨 아래)
        BatteryLivePlot.Refresh();
    }

    // 과거 기록 조회 끝 이벤트
    private void OnHistoryLoaded(HistoryChartData data)
    {
        DrawHistory(data.Points, data.BucketSeconds);
    }

    // 과거 기록 차트 그리기 함수 
    private void DrawHistory(IReadOnlyList<SummaryPoint> points, int bucketSeconds)
    {
        BatteryHistoryPlot.Plot.Clear();

        if (points.Count == 0) 
        {
            BatteryHistoryPlot.Plot.HideLegend();
            DrawHistoryCommon();
            return;
        }

        double[] xs = new double[points.Count];
        double[] avgs = new double[points.Count];
        double[] mins = new double[points.Count];
        double[] maxs = new double[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            xs[i] = points[i].BucketStart.AddSeconds(bucketSeconds / 2.0).ToOADate();   // 칸 가운데에 그림 (하루면 정오)
            avgs[i] = points[i].Avg;
            mins[i] = points[i].Min;
            maxs[i] = points[i].Max;
        }

        // 칸마다 막대 2개: 왼쪽 = 최저, 오른쪽 = 최고 (x 단위가 '일'이라 0.3 = 칸 폭의 30 %)
        double barWidth = bucketSeconds / 86400.0 * 0.3;
        List<ScottPlot.Bar> minBars = new List<ScottPlot.Bar>();
        List<ScottPlot.Bar> maxBars = new List<ScottPlot.Bar>();
        for (int i = 0; i < points.Count; i++)
        {
            minBars.Add(new ScottPlot.Bar
            {
                Position = xs[i] - barWidth / 2,
                Value = mins[i],
                Size = barWidth,
                FillColor = ScottPlot.Color.FromHex(Theme.Second),
            });
            maxBars.Add(new ScottPlot.Bar
            {
                Position = xs[i] + barWidth / 2,
                Value = maxs[i],
                Size = barWidth,
                FillColor = ScottPlot.Color.FromHex(Theme.Accent),
            });
        }
        var minPlot = BatteryHistoryPlot.Plot.Add.Bars(minBars);
        minPlot.LegendText = "최저";
        var maxPlot = BatteryHistoryPlot.Plot.Add.Bars(maxBars);
        maxPlot.LegendText = "최고";

        var avgLine = BatteryHistoryPlot.Plot.Add.Scatter(xs, avgs);   // 평균 = 점 + 선
        avgLine.Color = ScottPlot.Color.FromHex(Theme.Text);
        avgLine.LineWidth = 2;
        avgLine.MarkerSize = 8;
        avgLine.LegendText = "평균";

        DrawHistoryCommon();
        BatteryHistoryPlot.Plot.ShowLegend();
        BatteryHistoryPlot.Plot.Legend.Alignment = ScottPlot.Alignment.UpperRight;   
        BatteryHistoryPlot.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex(Theme.Card);
        BatteryHistoryPlot.Plot.Legend.FontColor = ScottPlot.Color.FromHex(Theme.Text);
        BatteryHistoryPlot.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex(Theme.Line);
        BatteryHistoryPlot.Refresh();
    }

    // 과거 기록 차트 공통 마무리 함수 (경고선, 시각 축, 범위, 다시 그리기)
    private void DrawHistoryCommon()
    {
        var warningLine = BatteryHistoryPlot.Plot.Add.HorizontalLine(60);   
        warningLine.LinePattern = ScottPlot.LinePattern.Dashed;
        warningLine.Color = ScottPlot.Color.FromHex(Theme.Caution);

        BatteryHistoryPlot.Plot.Axes.DateTimeTicksBottom();
        StylePlot(BatteryHistoryPlot);                       // 축을 새로 만든 뒤에 색·글꼴 (순서 중요)
        BatteryHistoryPlot.Plot.Axes.AutoScale();             // x축(시각)은 데이터에 맞춤
        BatteryHistoryPlot.Plot.Axes.SetLimitsY(0, 150);      // 0이 맨 아래, 위쪽은 범례 자리까지 150
        BatteryHistoryPlot.Refresh();
    }

    // 차트 어두운 색 + 한글 글꼴 함수 (Theme 색 사용)
    private static void StylePlot(ScottPlot.WPF.WpfPlot plot)
    {
        plot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex(Theme.Card);   // 차트 전체 바탕
        plot.Plot.DataBackground.Color = ScottPlot.Color.FromHex(Theme.Card);     // 데이터 영역 바탕
        plot.Plot.Axes.Color(ScottPlot.Color.FromHex(Theme.Muted));               // 축 선·눈금·글자
        plot.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex(Theme.Line);      // 격자
        plot.Plot.Font.Set(KoreanFont);         
        plot.Plot.Legend.FontName = KoreanFont; 
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
