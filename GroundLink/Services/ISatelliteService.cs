using GroundLink.Models;

namespace GroundLink.Services;

/// <summary>
/// 위성 데이터를 제공하는 서비스의 "약속(인터페이스)".
/// ViewModel은 이 인터페이스만 알고, 실제 구현이 가짜인지 서버 연결인지는 모른다.
/// 그래서 나중에 구현만 갈아끼우면 ViewModel 코드는 안 바꿔도 된다.
/// </summary>
public interface ISatelliteService
{
    
    // 화면에 보여줄 위성 목록 (서비스가 계속 값을 갱신함)
    IReadOnlyList<SatelliteStatus> Satellites { get; }

    // 위성 모드 변경 명령 (나중에 서버 POST /satellites/{id}/commands로 교체)
    // Task를 반환하는 이유: 진짜 서버 호출은 시간이 걸리는 비동기 작업이라 미리 맞춰둠
    Task SetModeAsync(string satelliteId, string mode);

    // 데이터 갱신 시작
    void Start();

    // 데이터 갱신 중지
    void Stop();


}

