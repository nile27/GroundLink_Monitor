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

    public void Start() { }
    public void Stop() { }

    public Task SetModeAsync(string satelliteId, string mode)
    {
        LastCommandId = satelliteId;
        LastCommandMode = mode;
        Items.First(s => s.Id == satelliteId).Mode = mode;
        return Task.CompletedTask;
    }

    /// <summary>테스트가 원할 때 알람을 발생시킨다</summary>
    public void FireAlarm(string id, string message)
        => AlarmRaised?.Invoke(this, new AlarmEvent(DateTime.Now, id, message));

    /// <summary>테스트가 원할 때 1초 갱신이 끝난 것처럼 알린다</summary>
    public void FireUpdated()
        => Updated?.Invoke(this, EventArgs.Empty);
}
