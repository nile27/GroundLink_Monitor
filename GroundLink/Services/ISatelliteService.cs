using GroundLink.Models;

namespace GroundLink.Services;

/// <summary>위성 데이터를 제공하는 서비스의 약속</summary>
public interface ISatelliteService
{
    // 위성 목록
    IReadOnlyList<SatelliteStatus> Satellites { get; }

    // 1초 갱신이 끝날 때마다 발생
    event EventHandler? Updated;

    // 알람이 생길 때마다 발생 (알람 내용 전달)
    event EventHandler<AlarmEvent>? AlarmRaised;

    void Start();
    void Stop();

    // 모드 변경 명령
    Task SetModeAsync(string satelliteId, string mode);

    // 위성 하나의 최근 기록
    IReadOnlyList<TelemetryPoint> GetHistory(string satelliteId);
}