using GroundLink.Models;
using ScottPlot.WPF;
using ScottPlot.Plottables;

namespace GroundLink.Charts;

// 차트 그리기 모음
public static class ChartDrawer
{
    private const string KoreanFont = "KoreanFont";   // 차트 한글 글꼴 이름

    public const int LiveSeconds = 300;

    public const int LiveMinSeconds = 60;

    // 한글 글꼴 등록 함수
    public static void RegisterKoreanFont()
    {
        ScottPlot.Fonts.AddFontFile(KoreanFont, @"C:\Windows\Fonts\malgun.ttf");
    }

  

    // 어두운 색 + 한글 글꼴 적용 함수
    public static void ApplyTheme(WpfPlot plot)
    {
        plot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex(Theme.Card);   // 차트 전체 바탕
        plot.Plot.DataBackground.Color = ScottPlot.Color.FromHex(Theme.Card);     // 데이터 영역 바탕
        plot.Plot.Axes.Color(ScottPlot.Color.FromHex(Theme.Muted));               // 축 선·눈금·글자
        plot.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex(Theme.Line);      // 격자

        plot.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex(Theme.Card);
        plot.Plot.Legend.FontColor = ScottPlot.Color.FromHex(Theme.Text);
        plot.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex(Theme.Line);

        plot.Plot.Font.Set(KoreanFont);           // 한글 글꼴
        plot.Plot.Legend.FontName = KoreanFont;   // 범례 글꼴
    }


    // 실시간 차트 준비 함수
    public static DataLogger SetupLive(WpfPlot plot, double yMin, double yMax, params double[] warnings)
    {
        plot.Plot.Clear();
        plot.Plot.Axes.DateTimeTicksBottom();

        DataLogger logger = plot.Plot.Add.DataLogger();   // 실시간 선
        logger.ManageAxisLimits = false;                  // 축 자동 조정 끔
        logger.LineWidth = 2;
        logger.Color = ScottPlot.Color.FromHex(Theme.Accent);

        foreach (double warning in warnings)
        {
            var warningLine = plot.Plot.Add.HorizontalLine(warning);   // 경고 기준선
            warningLine.LinePattern = ScottPlot.LinePattern.Dashed;
            warningLine.Color = ScottPlot.Color.FromHex(Theme.Caution);
        }

        ApplyTheme(plot);

        DateTime now = DateTime.Now;
        plot.Plot.Axes.SetLimits(now.ToOADate(), now.AddMinutes(1).ToOADate(), yMin, yMax);   // 연결 전 빈 차트
        plot.Refresh();
        return logger;
    }

    // 실시간 값 추가 함수
    public static void AppendLive(WpfPlot plot, DataLogger logger, DateTime time, double value)
    {
        logger.Add(time.ToOADate(), value);

        DateTime start = DateTime.FromOADate(logger.Data.Coordinates[0].X);   // 첫 수신 시각
        DateTime fiveMinutesAgo = time.AddSeconds(-LiveSeconds);
        DateTime left = fiveMinutesAgo > start ? fiveMinutesAgo : start;

        DateTime minRight = start.AddSeconds(LiveMinSeconds);
        DateTime right = time > minRight ? time : minRight;

        plot.Plot.Axes.SetLimitsX(left.ToOADate(), right.ToOADate());
        plot.Refresh();
    }

    // 과거 기록 차트 그리기 함수
    public static void DrawHistory(WpfPlot plot, IReadOnlyList<SummaryPoint> points, int bucketSeconds,
                                  double yMin, double yMax, params double[] warnings)
    {
        plot.Plot.Clear();

        if (points.Count == 0)   // 데이터 없음
        {
            plot.Plot.HideLegend();
            FinishTimeChart(plot, yMin, yMax, warnings);
            return;
        }

        double[] xs = new double[points.Count];
        double[] avgs = new double[points.Count];
        double[] mins = new double[points.Count];
        double[] maxs = new double[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            xs[i] = points[i].BucketStart.AddSeconds(bucketSeconds / 2.0).ToOADate();   // 칸 가운데
            avgs[i] = points[i].Avg;
            mins[i] = points[i].Min;
            maxs[i] = points[i].Max;
        }

        // 막대 폭 (칸 폭의 30 %)
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

        var avgLine = plot.Plot.Add.Scatter(xs, avgs);   // 평균선
        avgLine.Color = ScottPlot.Color.FromHex(Theme.Text);
        avgLine.LineWidth = 2;
        avgLine.MarkerSize = 8;
        avgLine.LegendText = "평균";

        plot.Plot.ShowLegend();
        plot.Plot.Legend.Alignment = ScottPlot.Alignment.UpperRight;   // 범례 오른쪽 위
        FinishTimeChart(plot, yMin, yMax, warnings);
    }

    // 시각 축 차트 공통 마무리 함수
    private static void FinishTimeChart(WpfPlot plot, double yMin, double yMax, double[] warnings)
    {

        foreach (double warning in warnings)
        {
            var warningLine = plot.Plot.Add.HorizontalLine(warning);   // 경고 기준선
            warningLine.LinePattern = ScottPlot.LinePattern.Dashed;
            warningLine.Color = ScottPlot.Color.FromHex(Theme.Caution);
        }


        plot.Plot.Axes.DateTimeTicksBottom();
        ApplyTheme(plot);
        plot.Plot.Axes.AutoScale();
        plot.Plot.Axes.SetLimitsY(yMin, yMax);     // y축 범위
        plot.Refresh();
    }
}
