namespace GroundLink.Models;

/// <summary>
/// 특정 시각의 텔레메트리 기록 한 점 (그래프의 점 하나).
/// record: 값만 담는 불변 데이터 타입. 생성 후 값이 안 바뀜
/// </summary>
public record TelemetryPoint(DateTime Time, double Battery, double Temperature);