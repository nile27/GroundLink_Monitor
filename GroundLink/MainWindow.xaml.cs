using System.Diagnostics;
using System.Windows;
using GroundLink.Data;
using GroundLink.Models;
using GroundLink.ViewModels;

namespace GroundLink;

/*
 * 메인 화면 (View).
 * 값·버튼 동작은 MainViewModel이 가짐 → XAML이 {Binding}으로 연결 (이 파일에 버튼 이벤트 없음).
 * 이 파일에 남는 것: ViewModel 만들기, 차트 그리기 (ScottPlot은 바인딩이 안 됨)
 *
 * 1. ViewModel 만들고 DataContext에 연결
 * 2. 차트 공통: 한글 글꼴, 어두운 색
 * 3. 실시간 차트 (지금은 가짜 데이터, 카드 12에서 수신 데이터로)
 * 4. 과거 기록 차트: ViewModel의 HistoryLoaded를 받아서 그림
 */
public partial class MainWindow : Window
{
    // DB 접속 정보 (나중에 설정 파일로 옮김)
    private const string ConnectionString = @"Server=localhost\SQLEXPRESS;Database=MonitorDB;Trusted_Connection=True;TrustServerCertificate=True;";
    private const string SatelliteId = "SAT-001";

    private const string KoreanFont = "KoreanFont";   // 차트 한글 글꼴 이름 (아래에서 malgun.ttf로 등록)

    private readonly MainViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();

        ScottPlot.Fonts.AddFontFile(KoreanFont, @"C:\Windows\Fonts\malgun.ttf");   // 한글 글꼴 파일 직접 등록 (차트 그리기 전에 1회)

        viewModel = new MainViewModel(new TelemetryRepository(ConnectionString), SatelliteId);
        viewModel.HistoryLoaded += OnHistoryLoaded;   // 과거 기록 조회 끝나면 차트 그리기
        DataContext = viewModel;                      // XAML의 {Binding ...}이 이 객체에서 값을 찾음

        DrawSampleChart();
        StylePlot(BatteryHistoryPlot);   // 버튼 누르기 전 빈 차트도 어둡게

        // 창이 뜬 뒤 어제 차트 (생성자에서 하면 창이 늦게 뜸). 버튼 누른 것과 같은 명령 실행
        Loaded += (_, _) => viewModel.SelectPeriodCommand.Execute(MainViewModel.PeriodDay);

        BatteryHistoryPlot.Plot.RenderManager.RenderFinished += (_, render) =>
            Debug.WriteLine($"Render {render.Elapsed.TotalMilliseconds:F1} ms");
    }

    // 고정 데이터 선 차트 함수 (1시간, 1분 간격 60점 + 경고선)
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
        StylePlot(BatteryLivePlot);                         // 축을 새로 만든 뒤에 색·글꼴 (순서 중요)

        var warningLine = BatteryLivePlot.Plot.Add.HorizontalLine(60);   // 경고 기준 60 %
        warningLine.LinePattern = ScottPlot.LinePattern.Dashed;
        warningLine.Color = ScottPlot.Color.FromHex(Theme.Caution);

        BatteryLivePlot.Plot.Axes.SetLimitsY(0, 100);      // 배터리 % → 0~100 고정 (0이 맨 아래)
        BatteryLivePlot.Refresh();
    }

    // 과거 기록 조회 끝 이벤트 (ViewModel → 화면)
    private void OnHistoryLoaded(HistoryChartData data)
    {
        DrawHistory(data.Points, data.BucketSeconds);
    }

    // 과거 기록 차트 그리기 함수 (칸마다 최저·최고 막대 좌우 + 평균 점선)
    private void DrawHistory(IReadOnlyList<SummaryPoint> points, int bucketSeconds)
    {
        BatteryHistoryPlot.Plot.Clear();

        if (points.Count == 0)   // 데이터 없음: 빈 차트 (글자는 ViewModel의 ErrorText)
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
        BatteryHistoryPlot.Plot.Legend.Alignment = ScottPlot.Alignment.UpperRight;   // 오른쪽 위 (막대를 안 가리게, y축 150까지 빈자리)
        BatteryHistoryPlot.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex(Theme.Card);
        BatteryHistoryPlot.Plot.Legend.FontColor = ScottPlot.Color.FromHex(Theme.Text);
        BatteryHistoryPlot.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex(Theme.Line);
        BatteryHistoryPlot.Refresh();
    }

    // 과거 기록 차트 공통 마무리 함수 (경고선, 시각 축, 범위, 다시 그리기)
    private void DrawHistoryCommon()
    {
        var warningLine = BatteryHistoryPlot.Plot.Add.HorizontalLine(60);   // 경고 기준 60 %
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
        plot.Plot.Font.Set(KoreanFont);          // 한글 글꼴 (ScottPlot은 WPF 글꼴이 아니라 자체로 글자를 그려서 따로 지정)
        plot.Plot.Legend.FontName = KoreanFont;  // 범례 글자도 따로 지정해야 한글이 나옴
    }
}
