using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.Diagnostics;
using GroundLink.Data;
using GroundLink.Models;
using Microsoft.Data.SqlClient;


namespace GroundLink;

// 메인 화면
public partial class MainWindow : Window
{

    // DB 접속 정보 (나중에 설정 파일로 옮김) 추후 수정 및 이동
    private const string ConnectionString = @"Server=localhost\SQLEXPRESS;Database=MonitorDB;Trusted_Connection=True;TrustServerCertificate=True;";
    private const string SatelliteId = "SAT-001";

    private const string KoreanFont = "KoreanFont";   // 차트 한글 글꼴 이름 (아래에서 malgun.ttf로 등록)

    private readonly TelemetryRepository repository = new TelemetryRepository(ConnectionString);
    public MainWindow()
    {
        InitializeComponent();

        ScottPlot.Fonts.AddFontFile(KoreanFont, @"C:\Windows\Fonts\malgun.ttf");   // 한글 글꼴 파일 직접 등록 (차트 그리기 전에 1회)

        SetStatus("연결 안 됨", Theme.Muted);
        SelectPeriod(DayButton);   // 처음 조회 기간 어제
        DrawSampleChart();
        StylePlot(BatteryHistoryPlot);   // 버튼 누르기 전 빈 차트도 어둡게

        Loaded += (_, _) => ShowHistorySummary(DateTime.Today.AddDays(-1), DateTime.Today, 3600);   // 창이 뜬 뒤 어제 차트 그리기 (생성자에서 하면 창이 늦게 뜸)

        BatteryHistoryPlot.Plot.RenderManager.RenderFinished += (_, render) =>
    Debug.WriteLine($"Render {render.Elapsed.TotalMilliseconds:F1} ms");

    }

    // 색 글자("#RRGGBB") → Brush 변환 함수
    private static Brush ToBrush(string hex)
    {
        BrushConverter converter = new BrushConverter();        // XAML이 색 글자를 바꿀 때 쓰는 변환기
        object? result = converter.ConvertFromString(hex);       // 여러 타입용 함수라 결과가 object?
        Brush brush = (Brush)result!;                            // Brush로 형 변환, ! = null 아님 표시
        return brush;
    }

    // 상태 배지 갱신 함수 (테두리·점·글자 같은 색)
    private void SetStatus(string text, string colorHex, string detail = "")
    {
        Brush brush = ToBrush(colorHex);
        StatusBorder.BorderBrush = brush;
        StatusDot.Fill = brush;
        StatusText.Foreground = brush;
        StatusText.Text = text;
        StatusDetailText.Text = detail;
    }

    // 현재 값 표시 함수
    private void ShowCurrentValues(double battery, double temperature)
    {
        BatteryText.Text = $"{battery:F1} %";
        TemperatureText.Text = $"{temperature:F1} °C";
    }

    // 기간 버튼 강조 함수 (누른 버튼만 Tag = true → 스타일이 하늘색)
    private void SelectPeriod(Button selected)
    {
        
        Button[] buttons = new Button[] { DayButton, WeekButton };

        foreach (Button button in buttons)
        {
            bool isSelected = (button == selected);   // 누른 버튼이면 true
            button.Tag = isSelected;
        }
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


    // 배터리 최신 값 조회 이벤트
    private void LoadLatestButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";

        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Telemetry? latest = repository.GetLatest(SatelliteId);
            stopwatch.Stop();
            Debug.WriteLine($"GetLatest {stopwatch.Elapsed.TotalMilliseconds:F1} ms");   // 측정 기록 (출력 창)

            if (latest == null)
            {
                ErrorText.Text = "데이터 없음";
                return;
            }

            ShowCurrentValues(latest.Battery, latest.Temperature);
            BatteryMarginText.Text = $"{latest.RecordedAt:HH:mm:ss} 기준";
        }
        catch (SqlException ex)
        {
            ErrorText.Text = $"DB 조회 실패: {ex.Message}";
        }
    }

    // 시뮬레이터 연결 이벤트
    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {

    }

    // 기간 버튼 이벤트 (버튼 3개 공용)
    private void PeriodButton_Click(object sender, RoutedEventArgs e)
    {
        SelectPeriod((Button)sender);

        if (sender == DayButton)
            ShowHistorySummary(DateTime.Today.AddDays(-1), DateTime.Today, 3600);   // 어제 0시 ~ 오늘 0시: 1시간 단위 (24줄)
        else if (sender == WeekButton)
            ShowHistorySummary(DateTime.Today.AddDays(-7), DateTime.Today, 86400);   // 지난 7일 (0시 기준): 하루 단위 (7줄) 최저·최고 막대 + 평균 선
    }

    // 과거 기록 원본 조회 + 그리기 함수 (짧은 기간용)
    private void ShowHistoryRaw(DateTime from, DateTime to)
    {
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            List<Telemetry> rows = repository.GetRange(SatelliteId, from, to);
            stopwatch.Stop();
            Debug.WriteLine($"GetRange {rows.Count}줄 {stopwatch.Elapsed.TotalMilliseconds:F1} ms");

            double[] xs = new double[rows.Count];
            double[] ys = new double[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                xs[i] = rows[i].RecordedAt.ToOADate();
                ys[i] = rows[i].Battery;
            }

            BatteryHistoryPlot.Plot.Clear();                     // 다시 누르면 겹치지 않게
            var rawLine = BatteryHistoryPlot.Plot.Add.Scatter(xs, ys);
            rawLine.Color = ScottPlot.Color.FromHex(Theme.Accent);
            BatteryHistoryPlot.Plot.HideLegend();                // 1주일에서 켠 범례 끄기
            DrawHistoryCommon();
        }
        catch (SqlException ex)
        {
            ErrorText.Text = $"DB 조회 실패: {ex.Message}";
        }
    }

    // 과거 기록 요약 조회 + 그리기 함수 (긴 기간용, 칸마다 최저·최고 막대 좌우 + 평균 점선)
    private void ShowHistorySummary(DateTime from, DateTime to, int bucketSeconds)
    {
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            List<SummaryPoint> points = repository.GetSummary(SatelliteId, from, to, bucketSeconds);
            stopwatch.Stop();
            Debug.WriteLine($"GetSummary {points.Count}줄 {stopwatch.Elapsed.TotalMilliseconds:F1} ms");

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

            BatteryHistoryPlot.Plot.Clear();

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
            BatteryHistoryPlot.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex(Theme.Card);
            BatteryHistoryPlot.Plot.Legend.FontColor = ScottPlot.Color.FromHex(Theme.Text);
            BatteryHistoryPlot.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex(Theme.Line);
            BatteryHistoryPlot.Refresh();
        }
        catch (SqlException ex)
        {
            ErrorText.Text = $"DB 조회 실패: {ex.Message}";
        }
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