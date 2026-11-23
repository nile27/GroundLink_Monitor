using GroundLink.Models;
using ScottPlot.WPF;

namespace GroundLink.Charts;

/*
 * 차트 그리기 모음 (View 쪽 도우미)
 * ScottPlot은 바인딩이 안 돼서 화면(MainWindow)이 이 함수들을 불러 직접 그림.
 * 차트마다 같은 코드를 반복하지 않게 plot을 매개변수로 받음 → 배터리·온도 차트가 같은 함수 사용
 *
 * 1. 한글 글꼴 등록, 어두운 색 + 한글 적용
 * 2. 실시간 차트 (지금은 가짜 데이터, 카드 12에서 수신 데이터로)
 * 3. 과거 기록 차트 (칸마다 최저·최고 막대 + 평균 점선)
 * 4. 공통: 경고선, 시각 축, y축 범위
 */
public static class ChartDrawer
{
    private const string KoreanFont = "KoreanFont";   // 차트 한글 글꼴 이름

    // 한글 글꼴 등록 함수 (앱 시작 시 1회, 이름 검색 대신 파일 직접 등록 → T-08)
    public static void RegisterKoreanFont()
    {
        ScottPlot.Fonts.AddFontFile(KoreanFont, @"C:\Windows\Fonts\malgun.ttf");
    }

    // 어두운 색 + 한글 글꼴 함수 (축을 새로 만든 뒤에 불러야 새 축까지 적용)
    public static void ApplyTheme(WpfPlot plot)
    {
        plot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex(Theme.Card);   // 차트 전체 바탕
        plot.Plot.DataBackground.Color = ScottPlot.Color.FromHex(Theme.Card);     // 데이터 영역 바탕
        plot.Plot.Axes.Color(ScottPlot.Color.FromHex(Theme.Muted));               // 축 선·눈금·글자
        plot.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex(Theme.Line);      // 격자

        plot.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex(Theme.Card);
        plot.Plot.Legend.FontColor = ScottPlot.Color.FromHex(Theme.Text);
        plot.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex(Theme.Line);

        plot.Plot.Font.Set(KoreanFont);           // 한글 글꼴 (ScottPlot은 자체 엔진으로 글자를 그려서 따로 지정)
        plot.Plot.Legend.FontName = KoreanFont;   // 범례 글자도 따로 지정해야 한글이 나옴
    }

    // 실시간 차트 가짜 데이터 함수 (1시간, 1분 간격 60점, 카드 12에서 교체)
    public static void DrawSampleLive(WpfPlot plot, double yMin, double yMax, double warning)
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

        plot.Plot.Clear();
        var line = plot.Plot.Add.Scatter(xs, ys);
        line.Color = ScottPlot.Color.FromHex(Theme.Accent);

        FinishTimeChart(plot, yMin, yMax, warning);
    }

    // 과거 기록 차트 그리기 함수 (칸마다 최저·최고 막대 좌우 + 평균 점선)
    public static void DrawHistory(WpfPlot plot, IReadOnlyList<SummaryPoint> points, int bucketSeconds,
                                   double yMin, double yMax, double warning)
    {
        plot.Plot.Clear();

        if (points.Count == 0)   // 데이터 없음: 빈 차트 (글자는 ViewModel의 ErrorText)
        {
            plot.Plot.HideLegend();
            FinishTimeChart(plot, yMin, yMax, warning);
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
        var minPlot = plot.Plot.Add.Bars(minBars);
        minPlot.LegendText = "최저";
        var maxPlot = plot.Plot.Add.Bars(maxBars);
        maxPlot.LegendText = "최고";

        var avgLine = plot.Plot.Add.Scatter(xs, avgs);   // 평균 = 점 + 선
        avgLine.Color = ScottPlot.Color.FromHex(Theme.Text);
        avgLine.LineWidth = 2;
        avgLine.MarkerSize = 8;
        avgLine.LegendText = "평균";

        plot.Plot.ShowLegend();
        plot.Plot.Legend.Alignment = ScottPlot.Alignment.UpperRight;   // 오른쪽 위 (막대를 안 가리게)
        FinishTimeChart(plot, yMin, yMax, warning);
    }

    // 시각 축 차트 공통 마무리 함수 (경고선, 시각 축, 색·글꼴, 범위, 다시 그리기)
    private static void FinishTimeChart(WpfPlot plot, double yMin, double yMax, double warning)
    {
        var warningLine = plot.Plot.Add.HorizontalLine(warning);   // 경고 기준선
        warningLine.LinePattern = ScottPlot.LinePattern.Dashed;
        warningLine.Color = ScottPlot.Color.FromHex(Theme.Caution);

        plot.Plot.Axes.DateTimeTicksBottom();
        ApplyTheme(plot);                          // 축을 새로 만든 뒤에 색·글꼴 (순서 중요)
        plot.Plot.Axes.AutoScale();                // x축(시각)은 데이터에 맞춤
        plot.Plot.Axes.SetLimitsY(yMin, yMax);     // y축 고정 (0이 맨 아래)
        plot.Refresh();
    }
}
