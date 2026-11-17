using GroundLink.Models;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;

namespace GroundLink.Charts;

/*
 * ScottPlot 차트 그리기.
 * DB·네트워크를 모름: 받은 데이터만 그림. 화면 요소라 UI 스레드에서만 호출.
 *
 * 1. 실시간 차트: x = 실제 시각, 처음 1분 폭 → 5분까지 늘어남 → 이후 최근 5분만. 여러 건을 받아 1번에 그림
 * 2. 과거 기록 (3시간·1일): 분 단위 평균선 → 충전·방전 파동 그대로
 * 3. 과거 기록 (1주일): 궤도 1바퀴 = 값 1개, 바퀴별 최고·최저 선 2개
 * 4. 공통: 어두운 색, 한글 글꼴, 경고선
 * 5. 과거 기록은 데이터가 빈 시간에서 선을 끊음 (이어 그리면 없는 데이터가 있는 것처럼 보임)
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
    /// 실시간 차트에 값 여러 개를 추가하고 1번만 다시 그림 (받을 때마다 그리면 초당 그리기 횟수가 수신 건수만큼 늘어남).
    /// x: start부터 1분 폭 → 5분까지 늘어남 → 이후 최근 5분만 (선이 항상 오른쪽 끝까지 참).
    /// y: 지금 보이는 값 범위에 맞춤. 최소 폭(minSpan)은 지켜서 작은 잡음이 크게 보이지 않게.
    /// </summary>
    public static void AppendLive(
        WpfPlot plot, DataLogger logger, DateTime start,
        IReadOnlyList<Telemetry> items, Func<Telemetry, double> value, double minSpan)
    {
        foreach (Telemetry item in items)
        {
            logger.Add(item.RecordedAt.ToOADate(), value(item));
        }

        DateTime time = items[^1].RecordedAt;
        DateTime fiveMinutesAgo = time.AddSeconds(-LiveSeconds);
        DateTime left = fiveMinutesAgo > start ? fiveMinutesAgo : start;
        DateTime minRight = start.AddSeconds(LiveMinSeconds);
        DateTime right = time > minRight ? time : minRight;
        plot.Plot.Axes.SetLimitsX(left.ToOADate(), right.ToOADate());

        // 보이는 범위의 최소·최대: 시각 순으로 쌓이므로 끝에서부터 왼쪽 끝까지만 봄
        // (처음부터 전부 훑으면 연결 시간이 길수록 느려짐)
        double leftX = left.ToOADate();
        var coordinates = logger.Data.Coordinates;
        double min = double.MaxValue;
        double max = double.MinValue;
        for (int i = coordinates.Count - 1; i >= 0 && coordinates[i].X >= leftX; i--)
        {
            min = Math.Min(min, coordinates[i].Y);
            max = Math.Max(max, coordinates[i].Y);
        }

        double middle = (min + max) / 2;
        double half = Math.Max((max - min) / 2 * 1.2, minSpan / 2);   // 위아래 10% 여유
        plot.Plot.Axes.SetLimitsY(middle - half, middle + half);

        plot.Refresh();   // 다시 그려 달라는 요청. 실제 그리기는 WPF 화면 갱신 때 (그리기 시간은 RenderFinished로 측정)
    }

    // ───────── 과거 기록 ─────────

    /// <summary>3시간·1일: 평균선 + 최소~최대 띠 + 경고선. 충전↗·방전↘ 모양이 그대로 보임.</summary>
    public static void DrawHistory(
        WpfPlot plot, List<SummaryPoint> points, string title, string unit, params double[] warningLines)
    {
        if (!BeginHistory(plot, points, title)) return;

        bool first = true;   // 범례는 첫 조각에만 (조각마다 넣으면 범례가 반복됨)
        foreach (List<SummaryPoint> part in SplitAtGaps(points))
        {
            double[] xs = part.Select(p => p.Bucket.ToOADate()).ToArray();   // DateTime → 숫자

            var band = plot.Plot.Add.FillY(xs,
                part.Select(p => p.Min).ToArray(), part.Select(p => p.Max).ToArray());   // 띠를 먼저 그려야 선이 위에 보임
            band.FillColor = Color.FromHex(Theme.Accent).WithAlpha(0.2);
            if (first) band.LegendText = "최소~최대";

            AddLine(plot, xs, part.Select(p => p.Avg).ToArray(), Theme.Accent, first ? "평균" : null, part.Count);
            first = false;
        }

        EndHistory(plot, unit, points.Min(p => p.Min), points.Max(p => p.Max), warningLines);
    }

    /// <summary>
    /// 1주일: 궤도 1바퀴 = 값 1개. 바퀴별 최고 선 + 최저 선 + 경고선.
    /// 선이 평평하면 매 바퀴 같은 폭으로 충전·방전 중 (안정). 최저 선이 내려가면 방전이 깊어진 것.
    /// 띠·점은 넣지 않음: 약 107바퀴라 점과 띠가 겹쳐서 선 2개가 안 읽힘.
    /// </summary>
    public static void DrawOrbitHistory(
        WpfPlot plot, List<SummaryPoint> points, string title, string unit, params double[] warningLines)
    {
        if (!BeginHistory(plot, points, title)) return;

        bool first = true;
        foreach (List<SummaryPoint> part in SplitAtGaps(points))
        {
            double[] xs = part.Select(p => p.Bucket.ToOADate()).ToArray();
            AddLine(plot, xs, part.Select(p => p.Max).ToArray(), Theme.Accent, first ? "바퀴별 최고 (햇빛 끝)" : null, part.Count);
            AddLine(plot, xs, part.Select(p => p.Min).ToArray(), Theme.Second, first ? "바퀴별 최저 (그늘 끝)" : null, part.Count);
            first = false;
        }

        EndHistory(plot, unit, points.Min(p => p.Min), points.Max(p => p.Max), warningLines);
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

    /// <summary>
    /// 구간 시작 시각 간격이 평소(가장 짧은 간격)의 1.5배를 넘는 곳에서 나눔.
    /// 예: 5분 구간인데 다음 구간이 16시간 뒤 → 그 사이는 데이터 없음 → 다른 조각.
    /// </summary>
    private static List<List<SummaryPoint>> SplitAtGaps(List<SummaryPoint> points)
    {
        var parts = new List<List<SummaryPoint>> { new() { points[0] } };
        if (points.Count == 1) return parts;

        double step = Enumerable.Range(1, points.Count - 1)
            .Min(i => (points[i].Bucket - points[i - 1].Bucket).TotalSeconds);   // 평소 간격 = 구간 크기

        for (int i = 1; i < points.Count; i++)
        {
            double gap = (points[i].Bucket - points[i - 1].Bucket).TotalSeconds;
            if (gap > step * 1.5) parts.Add(new List<SummaryPoint>());   // 빈 시간 → 새 조각
            parts[^1].Add(points[i]);
        }

        return parts;
    }

    /// <summary>선 1개. 점이 1개뿐인 조각은 선을 그을 수 없어서 점으로 표시. legend가 null이면 범례에 안 나옴.</summary>
    private static void AddLine(WpfPlot plot, double[] xs, double[] ys, string hex, string? legend, int count)
    {
        var line = plot.Plot.Add.Scatter(xs, ys);
        line.Color = Color.FromHex(hex);
        line.LineWidth = 2;
        line.MarkerSize = count == 1 ? 5 : 0;
        if (legend != null) line.LegendText = legend;
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
