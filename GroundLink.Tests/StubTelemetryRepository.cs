using GroundLink.Data;
using GroundLink.Models;

namespace GroundLink.Tests;

/// <summary>
/// 테스트 전용 저장소. 아무것도 저장하지 않고 빈 결과만 돌려준다.
/// </summary>
public class StubTelemetryRepository : ITelemetryRepository
{
    public Task SaveTelemetryAsync(IReadOnlyList<TelemetryRecord> records) => Task.CompletedTask;

    public Task SaveAlarmAsync(AlarmEvent alarm) => Task.CompletedTask;

    public Task<IReadOnlyList<HistoryPoint>> GetBatteryHistoryAsync(string satelliteId, DateTime from, DateTime to, int bucketSeconds)
        => Task.FromResult<IReadOnlyList<HistoryPoint>>(Array.Empty<HistoryPoint>());
}
