namespace GroundLink.Models;

/// <summary>
/// 알람 한 건. 발생한 뒤엔 바뀌지 않으므로 record(값만 담는 불변 타입).
/// </summary>
/// <param name="Time">발생 시각</param>
/// <param name="SatelliteId">알람이 난 위성</param>
/// <param name="Message">알람 내용 (한글)</param>
public record AlarmEvent(DateTime Time, string SatelliteId, string Message);
