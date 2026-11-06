using GroundLink.Models;
using GroundLink.Services;

namespace GroundLink.Tests;

/// <summary>
/// 테스트 전용 서비스. 타이머·랜덤 없이 테스트가 직접 조종한다.
/// </summary>
public class StubSatelliteService : ISatelliteService
{
    public List<SatelliteStatus> Items { get; } = new()
    {
        new SatelliteStatus { Id = "SAT-0001", Battery = 80 },
        new SatelliteStatus { Id = "SAT-0002", Battery = 50 },
    };

    public IReadOnlyList<SatelliteStatus> Satellites => Items;

    public event EventHandler? Updated;
    public event EventHandler<AlarmEvent>? AlarmRaised;

    // 마지막으로 받은 명령 (테스트에서 확인용)
    public string? LastCommandId { get; private set; }
    public string? LastCommandMode { get; private set; }

    /// <summary>받은 명령 전체 (위성 id, 모드) 순서대로</summary>
    public List<(string Id, string Mode)> Commands { get; } = new();

    /// <summary>true면 명령이 실패한다 (서버 오류·위성 거부 흉내)</summary>
    public bool FailCommands { get; set; }

    /// <summary>값을 넣으면 명령이 이 Task가 끝날 때까지 안 끝난다 (응답 대기 중 상태 확인용)</summary>
    public TaskCompletionSource? HoldCommands { get; set; }

    public void Start() { }
    public void Stop() { }

    public async Task SetModeAsync(string satelliteId, string mode)
    {
        LastCommandId = satelliteId;
        LastCommandMode = mode;
        Commands.Add((satelliteId, mode));

        if (HoldCommands is not null)
            await HoldCommands.Task;

        if (FailCommands)
            throw new InvalidOperationException("FAILED: 테스트용 실패");

        Items.First(s => s.Id == satelliteId).Mode = mode;
    }

    /// <summary>테스트가 원할 때 알람을 발생시킨다</summary>
    public void FireAlarm(string id, string message)
        => AlarmRaised?.Invoke(this, new AlarmEvent(DateTime.Now, id, message));

    /// <summary>테스트가 원할 때 1초 갱신이 끝난 것처럼 알린다</summary>
    public void FireUpdated()
        => Updated?.Invoke(this, EventArgs.Empty);
}
