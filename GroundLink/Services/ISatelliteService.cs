using GroundLink.Models;

namespace GroundLink.Services;

/// <summary>
/// 위성 데이터를 제공하는 서비스의 "약속".
/// ViewModel은 이것만 알고, 실제 구현이 가짜(Fake)인지 서버 연결인지는 모른다.
/// 그래서 나중에 서버 연결 버전으로 바꿔도 ViewModel은 그대로 둔다.
/// </summary>
public interface ISatelliteService
{
    /// <summary>위성 목록 (서비스가 값을 계속 갱신함)</summary>
    IReadOnlyList<SatelliteStatus> Satellites { get; }

    /// <summary>한 번의 갱신(1초)이 끝날 때마다 발생</summary>
    event EventHandler? Updated;

    /// <summary>알람이 생길 때마다 발생 (알람 내용을 같이 전달)</summary>
    event EventHandler<AlarmEvent>? AlarmRaised;

    void Start();
    void Stop();

    /// <summary>
    /// 위성 모드 변경 명령. 위성이 처리를 마치면 끝난다.
    /// 실패(서버 오류, 위성 거부, 시간 초과)하면 예외를 던진다 → 호출한 쪽이 화면을 되돌림.
    /// </summary>
    Task SetModeAsync(string satelliteId, string mode);
}
