namespace GroundLink.Performance;

/*
 * 성능 측정값 모으기 (1초 단위).
 * 수신 스레드(수신·저장)와 UI 스레드(화면 반영·차트 그리기)가 동시에 기록하므로 lock으로 보호.
 * Snapshot()이 지난 1초 값을 돌려주고 0으로 초기화.
 *
 * 1. 수신 건수
 * 2. DB 저장 시간 (10건 묶음 1번당)
 * 3. 수신 → 화면 반영까지 걸린 시간 (UI 지연)
 * 4. 차트 그리기 횟수와 시간 (ScottPlot이 실제로 그린 시간)
 */
public class PerfMonitor
{
    private readonly object sync = new();

    private int received;
    private int saves;
    private double saveMsSum;
    private int lags;
    private double lagMsSum;
    private double lagMsMax;
    private int renders;
    private double renderMsSum;
    private double renderMsMax;

    /// <summary>1건 수신 (수신 스레드).</summary>
    public void AddReceived()
    {
        lock (sync) received++;
    }

    /// <summary>DB 저장 1번에 걸린 시간 (수신 스레드).</summary>
    public void AddSave(TimeSpan elapsed)
    {
        lock (sync)
        {
            saves++;
            saveMsSum += elapsed.TotalMilliseconds;
        }
    }

    /// <summary>수신 시각부터 화면에 반영될 때까지 걸린 시간 (UI 스레드).</summary>
    public void AddUiLag(TimeSpan elapsed)
    {
        lock (sync)
        {
            lags++;
            lagMsSum += elapsed.TotalMilliseconds;
            lagMsMax = Math.Max(lagMsMax, elapsed.TotalMilliseconds);
        }
    }

    /// <summary>차트 1번 그리는 데 걸린 시간 (UI 스레드).</summary>
    public void AddRender(TimeSpan elapsed)
    {
        lock (sync)
        {
            renders++;
            renderMsSum += elapsed.TotalMilliseconds;
            renderMsMax = Math.Max(renderMsMax, elapsed.TotalMilliseconds);
        }
    }

    /// <summary>지난 측정 이후 값을 돌려주고 0으로 초기화. 1초마다 부르면 "초당" 값이 됨.</summary>
    public PerfSnapshot Snapshot()
    {
        lock (sync)
        {
            var snapshot = new PerfSnapshot(
                received,
                saves == 0 ? 0 : saveMsSum / saves,
                lags == 0 ? 0 : lagMsSum / lags,
                lagMsMax,
                renders,
                renders == 0 ? 0 : renderMsSum / renders,
                renderMsMax);

            received = saves = lags = renders = 0;
            saveMsSum = lagMsSum = lagMsMax = renderMsSum = renderMsMax = 0;
            return snapshot;
        }
    }
}

/// <summary>1초 동안의 측정값. ms는 평균, Max는 최대.</summary>
public record PerfSnapshot(
    int Received,
    double SaveMs,
    double UiLagMs,
    double UiLagMaxMs,
    int Renders,
    double RenderMs,
    double RenderMaxMs);
