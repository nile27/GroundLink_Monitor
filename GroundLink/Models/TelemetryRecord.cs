namespace GroundLink.Models;

/// <summary>
/// DB Telemetry 테이블에 저장되는 한 줄.
/// SatelliteStatus(화면용, 값이 계속 바뀜)를 특정 순간에 복사해둔 것.
/// </summary>
public record TelemetryRecord(
    string SatelliteId,
    DateTime RecordedAt,
    double Battery,
    double Temperature,
    string Mode,
    bool InContact);
