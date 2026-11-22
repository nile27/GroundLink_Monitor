using System.Diagnostics;
using GroundLink.Data;
using Xunit.Abstractions;

namespace GroundLink.Tests.Performance;

// DB 최신 값 조회 시간 측정 (실제 DB 필요)
public class GetLatestTimingTests
{
    private const string ConnectionString =
        @"Server=localhost\SQLEXPRESS;Database=MonitorDB;Trusted_Connection=True;TrustServerCertificate=True;";
    private const string SatelliteId = "SAT-001";

    private readonly ITestOutputHelper output;

    public GetLatestTimingTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    // 첫 호출 / 이후 10회 중앙값 측정 함수
    [Fact]
    [Trait("Category", "Perf")]
    public void 최신값_조회_시간()
    {
        TelemetryRepository repository = new TelemetryRepository(ConnectionString);

        // 첫 호출: 연결 풀이 비어 있어 연결을 새로 맺음 → 따로 기록
        Stopwatch first = Stopwatch.StartNew();
        var firstResult = repository.GetLatest(SatelliteId);
        first.Stop();
        Assert.NotNull(firstResult);

        // 이후 10회
        List<double> times = new List<double>();
        for (int i = 0; i < 10; i++)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            var result = repository.GetLatest(SatelliteId);
            stopwatch.Stop();

            Assert.NotNull(result);   // 결과가 있는지만 검사 (시간은 검사하지 않음)
            times.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        double median = (times[4] + times[5]) / 2;   // 10개라 가운데 두 값의 평균

        output.WriteLine($"첫 호출 {first.Elapsed.TotalMilliseconds:F1} ms");
        output.WriteLine($"이후 10회 중앙값 {median:F1} ms (최소 {times[0]:F1}, 최대 {times[^1]:F1})");
    }
}