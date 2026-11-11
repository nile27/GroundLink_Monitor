using GroundLink.Models;
using ScottPlot.Plottables;
using ScottPlot.WPF;

namespace GroundLink.Charts;

/*
 * ScottPlot 차트 그리기.
 * DB·네트워크를 모름: 받은 데이터만 그림. 화면 요소라 UI 스레드에서만 호출.
 */
public static class ChartDrawer
{
    public const int LiveSeconds = 300;          // 실시간 차트에 보이는 폭 (초, 5분)
    private const string FontName = "KoreanFont";

    /// <summary>한글 글꼴 등록. 앱 시작 시 1회.</summary>
    public static void RegisterKoreanFont()
    {
        // 이름 검색 실패 시 한글 없는 기본 글꼴로 대체되므로 파일을 직접 등록
        ScottPlot.Fonts.AddFontFile(FontName, @"C:\Windows\Fonts\malgun.ttf");
    }

    /// <summary>과거 기록: 평균선 + 최소~최대 띠 + 경고선. 배터리·온도 공통.</summary>
    public static void DrawHistory(
        WpfPlot plot, List<SummaryPoint> points, string title, string unit, params double[] warningLines)
    {
        plot.Plot.Clear();

        // 빈 배열로 그리면 에러 가능
        if (points.Count == 0)
        {
            plot.Plot.Title($"{title}: 데이터 없음");
            plot.Plot.Font.Set(FontName);
            plot.Refresh();
            return;
        }

        double[] xs = points.Select(p => p.Bucket.ToOADate()).ToArray();   // DateTime → 숫자
        double[] avgs = points.Select(p => p.Avg).ToArray();
        double[] mins = points.Select(p => p.Min).ToArray();
        double[] maxs = points.Select(p => p.Max).ToArray();

        var band = plot.Plot.Add.FillY(xs, mins, maxs);   // 띠를 먼저 그려야 선이 위에 보임
        band.LegendText = "최소~최대";

        var avgLine = plot.Plot.Add.Scatter(xs, avgs);
        avgLine.LegendText = "평균";
        avgLine.MarkerSize = 0;   // 점 없이 선만
        avgLine.LineWidth = 2;

        for (int i = 0; i < warningLines.Length; i++)
        {
            AddWarningLine(plot, warningLines[i], i == 0 ? "경고 기준" : null);   // 범례는 한 번만
        }

        plot.Plot.Title(title);
        plot.Plot.YLabel(unit);
        plot.Plot.ShowLegend();
        plot.Plot.Axes.DateTimeTicksBottom();   // x축 날짜 표시
        plot.Plot.Axes.AutoScale();

        // y축은 데이터 범위에 맞춤 (경고선까지 넣으면 띠가 납작해짐). 경고선은 범위 안에 들어올 때만 보임
        plot.Plot.Axes.SetLimitsY(mins.Min() - 2, maxs.Max() + 2);
        plot.Plot.Font.Set(FontName);           // 날짜 축 생성 후 호출해야 눈금까지 적용
        plot.Plot.Legend.FontName = FontName;
        plot.Refresh();                         // 화면 반영 (없으면 안 보임)
    }

    /// <summary>
    /// 실시간 차트 준비: 데이터 로거 + 경고선 + 축 범위. 만든 로거 반환.
    /// x = 연결 후 경과 초, y = 값. yMin~yMax는 평소 값 범위 (벗어나면 AppendLive가 넓힘).
    /// </summary>
    public static DataLogger SetupLive(
        WpfPlot plot, string title, string unit, double yMin, double yMax, params double[] warningLines)
    {
        plot.Plot.Clear();

        var logger = plot.Plot.Add.DataLogger();
        logger.ManageAxisLimits = false;   // 축 범위는 AppendLive에서 직접 관리
        logger.LineWidth = 2;

        foreach (double y in warningLines)
        {
            AddWarningLine(plot, y, null);
        }

        plot.Plot.Title(title);
        plot.Plot.YLabel(unit);
        plot.Plot.XLabel("연결 후 경과 시간 (초)");
        plot.Plot.Axes.SetLimits(0, LiveSeconds, yMin, yMax);
        plot.Plot.Font.Set(FontName);
        plot.Refresh();

        return logger;
    }

    /// <summary>
    /// 실시간 차트에 값 1개 추가 후 다시 그림.
    /// x: 처음 5분은 왼쪽(0초)부터 채우고, 5분이 지나면 최근 5분만 보이게 밀어냄.
    /// y: 값이 범위를 벗어나면 넓힘 (좁히지는 않음).
    /// </summary>
    public static void AppendLive(WpfPlot plot, DataLogger logger, double seconds, double value)
    {
        logger.Add(seconds, value);

        double left = Math.Max(0, seconds - LiveSeconds);
        plot.Plot.Axes.SetLimitsX(left, left + LiveSeconds);

        var limits = plot.Plot.Axes.GetLimits();
        if (value < limits.Bottom || value > limits.Top)
        {
            plot.Plot.Axes.SetLimitsY(Math.Min(limits.Bottom, value - 1), Math.Max(limits.Top, value + 1));
        }

        plot.Refresh();
    }

    /// <summary>주황 점선 가로선. legend가 있으면 범례에 표시.</summary>
    private static void AddWarningLine(WpfPlot plot, double y, string? legend)
    {
        var line = plot.Plot.Add.HorizontalLine(y);
        line.LinePattern = ScottPlot.LinePattern.Dashed;
        line.Color = ScottPlot.Colors.Orange;
        if (legend != null) line.LegendText = legend;
    }
}
