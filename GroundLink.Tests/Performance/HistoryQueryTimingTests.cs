using System.Diagnostics;
using GroundLink.Data;
using Xunit.Abstractions;

namespace GroundLink.Tests.Performance;

// 1주일 원본 조회 시간 측정 (실제 DB 필요)
public class HistoryQueryTimingTests
{
    private const string ConnectionString =
        @"Server=localhost\SQLEXPRESS;Database=MonitorDB;Trusted_Connection=True;TrustServerCertificate=True;";
    private const string SatelliteId = "SAT-001";

    private readonly ITestOutputHelper output;

    public HistoryQueryTimingTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    // 1주일 원본: 5회 중앙값 측정 함수 (첫 1회 버림)
    [Fact]
    [Trait("Category", "Perf")]
    public void WeekRaw()
    {
        TelemetryRepository repository = new TelemetryRepository(ConnectionString);
        DateTime from = DateTime.Now.AddDays(-7);
        repository.GetRange(SatelliteId, from);   // 첫 1회 버림

        List<double> times = new List<double>();
        int rowCount = 0;
        for (int i = 0; i < 5; i++)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            rowCount = repository.GetRange(SatelliteId, from).Count;
            stopwatch.Stop();
            times.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        Assert.True(rowCount > 0);
        times.Sort();
        output.WriteLine($"원본 {rowCount}줄, 5회 중앙값 {times[2]:F1} ms (최소 {times[0]:F1}, 최대 {times[^1]:F1})");
    }

    // 1주일 요약(5분): 5회 중앙값 측정 함수 (첫 1회 버림)
    [Fact]
    [Trait("Category", "Perf")]
    public void WeekSummary()
    {
        TelemetryRepository repository = new TelemetryRepository(ConnectionString);
        DateTime from = DateTime.Now.AddDays(-7);
        repository.GetSummary(SatelliteId, from, 300);   // 첫 1회 버림

        List<double> times = new List<double>();
        int rowCount = 0;
        for (int i = 0; i < 5; i++)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            rowCount = repository.GetSummary(SatelliteId, from, 300).Count;
            stopwatch.Stop();
            times.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        Assert.True(rowCount > 0);
        times.Sort();
        output.WriteLine($"요약 {rowCount}줄, 5회 중앙값 {times[2]:F1} ms (최소 {times[0]:F1}, 최대 {times[^1]:F1})");
    }

    // 한 달 원본: 5회 중앙값 측정 함수 (첫 1회 버림)
    [Fact]
    [Trait("Category", "Perf")]
    public void MonthRaw()
    {
        TelemetryRepository repository = new TelemetryRepository(ConnectionString);
        DateTime from = DateTime.Today.AddDays(-30);
        repository.GetRange(SatelliteId, from);   // 첫 1회 버림

        List<double> times = new List<double>();
        int rowCount = 0;
        for (int i = 0; i < 5; i++)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            rowCount = repository.GetRange(SatelliteId, from).Count;
            stopwatch.Stop();
            times.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        Assert.True(rowCount > 0);
        times.Sort();
        output.WriteLine($"원본 {rowCount}줄, 5회 중앙값 {times[2]:F1} ms (최소 {times[0]:F1}, 최대 {times[^1]:F1})");
    }

    // 한 달 요약(하루 단위): 5회 중앙값 측정 함수 (첫 1회 버림)
    [Fact]
    [Trait("Category", "Perf")]
    public void MonthSummary()
    {
        TelemetryRepository repository = new TelemetryRepository(ConnectionString);
        DateTime from = DateTime.Today.AddDays(-30);
        DateTime to = DateTime.Today;
        repository.GetSummaries(SatelliteId, from, to, 86400);   // 첫 1회 버림

        List<double> times = new List<double>();
        int rowCount = 0;
        for (int i = 0; i < 5; i++)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            rowCount = repository.GetSummaries(SatelliteId, from, to, 86400).Battery.Count;
            stopwatch.Stop();
            times.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        Assert.True(rowCount > 0);
        times.Sort();
        output.WriteLine($"요약 {rowCount}줄, 5회 중앙값 {times[2]:F1} ms (최소 {times[0]:F1}, 최대 {times[^1]:F1})");
    }
}
