namespace GroundLink.Models;

/// <summary>
/// 알람 한 건. 발생한 뒤엔 바뀌지 않으므로 record.
/// </summary>
public record AlarmEvent(DateTime Time, string SatelliteId, string Message);