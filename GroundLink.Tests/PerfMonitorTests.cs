using GroundLink.Performance;

namespace GroundLink.Tests;

/*
 * 성능 측정값 모으기 테스트: 평균·최대 계산과, 꺼낸 뒤 0으로 초기화되는지.
 */
public class PerfMonitorTests
{
    [Fact]
    public void 평균과_최대를_계산하고_꺼내면_초기화()
    {
        var perf = new PerfMonitor();
        perf.AddReceived();
        perf.AddReceived();
        perf.AddRender(TimeSpan.FromMilliseconds(2));
        perf.AddRender(TimeSpan.FromMilliseconds(4));
        perf.AddUiLag(TimeSpan.FromMilliseconds(10));

        PerfSnapshot first = perf.Snapshot();
        PerfSnapshot second = perf.Snapshot();

        Assert.Equal(2, first.Received);
        Assert.Equal(2, first.Renders);
        Assert.Equal(3.0, first.RenderMs, precision: 6);
        Assert.Equal(4.0, first.RenderMaxMs, precision: 6);
        Assert.Equal(10.0, first.UiLagMs, precision: 6);

        Assert.Equal(0, second.Received);
        Assert.Equal(0, second.Renders);
        Assert.Equal(0.0, second.SaveMs);   // 기록이 없으면 0 (0으로 나누지 않음)
    }

    [Fact]
    public void 여러_스레드가_동시에_기록해도_건수가_맞음()
    {
        var perf = new PerfMonitor();

        Parallel.For(0, 10_000, _ => perf.AddReceived());

        Assert.Equal(10_000, perf.Snapshot().Received);
    }
}
