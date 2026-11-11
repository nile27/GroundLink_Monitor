using GroundLink.Models;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;

namespace GroundLink.Charts;

/*
 * ScottPlot 차트 그리기.
 * DB·네트워크를 모름: 받은 데이터만 그림. 화면 요소라 UI 스레드에서만 호출.
 *
 * 1. 실시간 차트: x = 실제 시각, 처음 1분 폭 → 5분까지 늘어남 → 이후 최근 5분만
 * 2. 과거 기록 (3시간·1일): 분 단위 평균선 → 충전·방전 파동 그대로
 * 3. 과거 기록 (1주일): 궤도 1바퀴 = 점 1개, 바퀴별 최고·최저 선
 * 4. 공통: 어두운 색, 한글 글꼴, 경고선
 */
public static class ChartDrawer
{
    public const int LiveSeconds = 300;      // 실시간 차트 최대 폭 (초, 5분)
    private const int LiveMinSeconds = 60;   // 시작 폭: 5분으로 시작하면 처음 몇 초가 왼쪽 끝에 몰려 보임
    private const string FontName = "KoreanFont";

    /// <summary>한글 글꼴 등록. 앱 시작 시 1회.</summary>
    public static void RegisterKoreanFont()
    {
        // 이름 검색 실패 시 한글 없는 기본 글꼴로 대체되므로 파일을 직접 등록
        Fonts.AddFontFile(FontName, @"C:\Windows\Fonts\malgun.ttf");
    }

    // ───────── 실시간 ─────────

    /// <summary>
    /// 실시간 차트 준비: 데이터 로거 + 경고선. 만든 로거 반환.
    /// yMin~yMax: 첫 수신 전 빈 차트의 범위.
    /// </summary>
    public static DataLogger SetupLive(
        WpfPlot plot, string title, string unit, double yMin, double yMax, params double[] warningLines)
    {
        plot.Plot.Clear();
        plot.Plot.Axes.DateTimeTicksBottom();   // 로거보다 먼저: 로거가 날짜 축을 쓰도록

        var logger = plot.Plot.Add.DataLogger();
        logger.ManageAxisLimits = false;   // 축 범위는 AppendLive에서 직접 관리
        logger.LineWidth = 2;
        logger.Color = Color.FromHex(Theme.Accent);

        foreach (double y in warningLines)
        {
            AddWarningLine(plot, y, null);
        }

        plot.Plot.Title(title);
        plot.Plot.YLabel(unit);

        DateTime now = DateTime.Now;
        plot.Plot.Axes.SetLimits(now.ToOADate(), now.AddSeconds(LiveMinSeconds).ToOADate(), yMin, yMax);

        ApplyTheme(plot);
        plot.Refresh();
        return logger;
    }

    /// <summary>
    /// 실시간 차트에 값 1개 추가 후 다시 그림.
    /// x: start부터 1분 폭 → 5분까지 늘어남 → 이후 최근 5분만 (선이 항상 오른쪽 끝까지 참).
    /// y: 지금 보이는 값 범위에 맞춤. 최소 폭(minSpan)은 지켜서 작은 잡음이 크게 보이지 않게.
    /// </summary>
    public static void AppendLive(
        WpfPlot plot, DataLogger logger, DateTime start, DateTime time, double value, double minSpan)
    {
        logger.Add(time.ToOADate(), value);

        DateTime fiveMinutesAgo = time.AddSeconds(-LiveSeconds);
        DateTime left = fiveMinutesAgo > start ? fiveMinutesAgo : start;
        DateTime minRight = start.AddSeconds(LiveMinSeconds);
        DateTime right = time > minRight ? time : minRight;
        plot.Plot.Axes.SetLimitsX(left.ToOADate(), right.ToOADate());

        double leftX = left.ToOADate();
        var visible = logger.Data.Coordinates.Where(c => c.X >= leftX).Select(c => c.Y).ToList();
        double min = visible.Min();
        double max = visible.Max();
        double middle = (min + max) / 2;
        double half = Math.Max((max - min) / 2 * 1.2, minSpan / 2);   // 위아래 10% 여유
        plot.Plot.Axes.SetLimitsY(middle - half, middle + half);

        plot.Refresh();
    }

    // ───────── 과거 기록 ─────────

    /// <summary>3시간·1일: 평균선 + 최소~최대 띠 + 경고선. 충전↗·방전↘ 모양이 그대로 보임.</summary>
    public static void DrawHistory(
        WpfPlot plot, List<SummaryPoint> points, string title, string unit, params double[] warningLines)
    {
        if (!BeginHistory(plot, points, title)) return;

        double[] xs = points.Select(p => p.Bucket.ToOADate()).ToArray();   // DateTime → 숫자
        double[] mins = points.Select(p => p.Min).ToArray();
        double[] maxs = points.Select(p => p.Max).ToArray();

        var band = plot.Plot.Add.FillY(xs, mins, maxs);   // 띠를 먼저 그려야 선이 위에 보임
        band.FillColor = Color.FromHex(Theme.Accent).WithAlpha(0.2);
        band.LegendText = "최소~최대";

        AddLine(plot, xs, points.Select(p => p.Avg).ToArray(), Theme.Accent, "평균", 0);

        EndHistory(plot, unit, mins.Min(), maxs.Max(), warningLines);
    }

    /// <summary>
    /// 1주일: 궤도 1바퀴 = 점 1개. 바퀴별 최고 선 + 최저 선 + 그 사이 띠 + 경고선.
    /// 선이 평평하면 매 바퀴 같은 폭으로 충전·방전 중 (안정). 최저 선이 내려가면 방전이 깊어진 것.
    /// </summary>
    public static void DrawOrbitHistory(
        WpfPlot plot, List<SummaryPoint> points, string title, string unit, params double[] warningLines)
    {
        if (!BeginHistory(plot, points, title)) return;

        double[] xs = points.Select(p => p.Bucket.ToOADate()).ToArray();
        double[] mins = points.Select(p => p.Min).ToArray();
        double[] maxs = points.Select(p => p.Max).ToArray();

        var band = plot.Plot.Add.FillY(xs, mins, maxs);
        band.FillColor = Color.FromHex(Theme.Accent).WithAlpha(0.1);
        band.LegendText = "한 바퀴 동안 오간 범위";

        AddLine(plot, xs, maxs, Theme.Accent, "바퀴별 최고", 4);
        AddLine(plot, xs, mins, Theme.Second, "바퀴별 최저", 4);   // 점: 바퀴 하나하나가 구분되게

        EndHistory(plot, unit, mins.Min(), maxs.Max(), warningLines);
    }

    /// <summary>과거 기록 공통 시작. 데이터가 없으면 안내만 그리고 false.</summary>
    private static bool BeginHistory(WpfPlot plot, List<SummaryPoint> points, string title)
    {
        plot.Plot.Clear();

        if (points.Count == 0)   // 빈 배열로 그리면 에러 가능
        {
            plot.Plot.Title($"{title}: 데이터 없음");
            ApplyTheme(plot);
            plot.Refresh();
            return false;
        }

        plot.Plot.Title(title);
        return true;
    }

    /// <summary>과거 기록 공통 마무리: 경고선, 날짜 축, y 범위, 색.</summary>
    private static void EndHistory(WpfPlot plot, string unit, double dataMin, double dataMax, double[] warningLines)
    {
        for (int i = 0; i < warningLines.Length; i++)
        {
            AddWarningLine(plot, warningLines[i], i == 0 ? "경고 기준" : null);   // 범례는 한 번만
        }

        plot.Plot.YLabel(unit);
        plot.Plot.ShowLegend(Alignment.LowerRight);
        plot.Plot.Axes.DateTimeTicksBottom();   // x축 날짜 표시
        plot.Plot.Axes.AutoScaleX();

        // y축은 데이터 범위에 맞춤 (경고선까지 넣으면 선이 납작해짐). 경고선은 범위 안에 들어올 때만 보임
        plot.Plot.Axes.SetLimitsY(dataMin - 2, dataMax + 2);

        ApplyTheme(plot);   // 날짜 축 생성 후 호출해야 눈금 글꼴·색까지 적용
        plot.Refresh();     // 화면 반영 (없으면 안 보임)
    }

    // ───────── 공통 ─────────

    /// <summary>선 1개. markerSize 0이면 점 없이 선만.</summary>
    private static void AddLine(WpfPlot plot, double[] xs, double[] ys, string hex, string legend, float markerSize)
    {
        var line = plot.Plot.Add.Scatter(xs, ys);
        line.Color = Color.FromHex(hex);
        line.LineWidth = 2;
        line.MarkerSize = markerSize;
        line.LegendText = legend;
    }

    /// <summary>주황 점선 가로선. legend가 있으면 범례에 표시.</summary>
    private static void AddWarningLine(WpfPlot plot, double y, string? legend)
    {
        var line = plot.Plot.Add.HorizontalLine(y);
        line.LinePattern = LinePattern.Dashed;
        line.Color = Color.FromHex(Theme.Caution);
        if (legend != null) line.LegendText = legend;
    }

    /// <summary>어두운 색 + 한글 글꼴. 축을 새로 만든 뒤에 호출해야 새 축까지 적용됨.</summary>
    private static void ApplyTheme(WpfPlot plot)
    {
        Plot p = plot.Plot;

        p.FigureBackground.Color = Color.FromHex(Theme.Card);
        p.DataBackground.Color = Color.FromHex(Theme.Card);
        p.Axes.Color(Color.FromHex(Theme.Muted));
        p.Axes.Title.Label.ForeColor = Color.FromHex(Theme.Text);
        p.Grid.MajorLineColor = Color.FromHex(Theme.Line);

        p.Legend.BackgroundColor = Color.FromHex(Theme.Background);
        p.Legend.FontColor = Color.FromHex(Theme.Text);
        p.Legend.OutlineColor = Color.FromHex(Theme.Line);

        p.Font.Set(FontName);
        p.Legend.FontName = FontName;
    }
}
