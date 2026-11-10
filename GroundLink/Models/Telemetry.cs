namespace GroundLink.Models;

/*
 * 텔레메트리 1건 (위성 → 지상). 시뮬레이터의 Telemetry와 같은 형식 유지 필요.
 *
 * SatelliteId  : 위성 ID (예: SAT-001)
 * RecordedAt   : 측정 시각 (로컬 시간)
 * Battery      : 배터리 잔량 (%, 0~100)
 * Temperature  : 배터리 온도 (°C)
 */
public record Telemetry(string SatelliteId, DateTime RecordedAt, double Battery, double Temperature);
