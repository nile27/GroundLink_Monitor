using GroundLink.Models;

namespace GroundLink.Data;

public interface ITelemetryRepository
{
    // 여러 위성의 텔레메트리 저장
    Task SaveTelemetryAsync(IReadOnlyList<TelemetryRecord> records);

    // 알람 한 건 저장
    Task SaveAlarmAsync(AlarmEvent alarm);

    // 위성 하나의 기간 기록 조회 (W8-5 차트용)
    Task<IReadOnlyList<TelemetryPoint>> GetHistoryAsync(string satelliteId, DateTime from, DateTime to);
}