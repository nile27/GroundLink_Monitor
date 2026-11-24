using GroundLink.Data;

namespace GroundLink.Tests.Data;

// 하루 요약 값 확인 (실제 DB 필요)
public class DailySummaryTests
{
    private const string ConnectionString =
        @"Server=localhost\SQLEXPRESS;Database=MonitorDB;Trusted_Connection=True;TrustServerCertificate=True;";
    private const string SatelliteId = "SAT-001";

    // 지난 30일 원본 요약과 하루 요약 비교 함수
    [Fact]
    public async Task DailyMatchesRaw()
    {
        TelemetryRepository repository = new TelemetryRepository(ConnectionString);
        DateTime from = DateTime.Today.AddDays(-30);
        DateTime to = DateTime.Today;

        await repository.FillDailyAsync(SatelliteId, from, to);
        var raw = repository.GetSummaries(SatelliteId, from, to, 86400);
        var daily = await repository.GetDailyAsync(SatelliteId, from, to);

        Assert.True(raw.Battery.Count > 0);
        Assert.Equal(raw.Battery.Count, daily.Battery.Count);
        for (int i = 0; i < raw.Battery.Count; i++)
        {
            Assert.Equal(raw.Battery[i].BucketStart, daily.Battery[i].BucketStart);
            Assert.Equal(raw.Battery[i].Min, daily.Battery[i].Min);
            Assert.Equal(raw.Battery[i].Max, daily.Battery[i].Max);
            Assert.Equal(raw.Battery[i].Avg, daily.Battery[i].Avg, 6);
            Assert.Equal(raw.Temperature[i].Min, daily.Temperature[i].Min);
            Assert.Equal(raw.Temperature[i].Max, daily.Temperature[i].Max);
            Assert.Equal(raw.Temperature[i].Avg, daily.Temperature[i].Avg, 6);
        }
    }
}
