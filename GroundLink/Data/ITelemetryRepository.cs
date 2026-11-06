using GroundLink.Models;

namespace GroundLink.Data;

/// <summary>
/// DB 저장소의 "약속".
/// 지금은 SQL을 직접 쓰는 구현(SqlTelemetryRepository) 하나지만,
/// 나중에 EF Core 버전이나 서버 API 버전으로 바꿔 끼울 수 있다.
/// </summary>
public interface ITelemetryRepository
{
    /// <summary>여러 위성의 텔레메트리를 한 번에 저장</summary>
    Task SaveTelemetryAsync(IReadOnlyList<TelemetryRecord> records);

    /// <summary>알람 한 건 저장</summary>
    Task SaveAlarmAsync(AlarmEvent alarm);

    /// <summary>
    /// 위성 하나의 배터리 기록을 기간으로 조회.
    /// bucketSeconds초 단위로 묶어서 구간별 최소·최대·평균만 돌려준다 (차트용).
    /// </summary>
    Task<IReadOnlyList<HistoryPoint>> GetBatteryHistoryAsync(string satelliteId, DateTime from, DateTime to, int bucketSeconds);
}
