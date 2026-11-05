namespace GroundLink.Models;

/// <summary>DB Telemetry 테이블 한 줄</summary>
public record TelemetryRecord(
    string SatelliteId,
    DateTime RecordedAt,
    double Battery,
    double Temperature,
    string Mode,
    bool InContact);