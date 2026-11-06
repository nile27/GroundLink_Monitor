using System.Windows.Threading;
using GroundLink.Models;

namespace GroundLink.Services;

/// <summary>
/// 서버 없이 개발하기 위한 가짜 위성 서비스.
/// 위성 N대를 만들고, 1초마다 값을 조금씩 바꾸고, 조건이 맞으면 알람을 발생시킨다.
/// </summary>
public class FakeSatelliteService : ISatelliteService
{
    private readonly Random _random = new();
    private readonly List<SatelliteStatus> _satellites = new();

    // DispatcherTimer: UI 스레드에서 실행되는 타이머.
    // 화면에 묶인 값을 바꾸므로 UI 스레드에서 바꾸는 게 안전하다.
    private readonly DispatcherTimer _timer;

    public IReadOnlyList<SatelliteStatus> Satellites => _satellites;

    public event EventHandler? Updated;
    public event EventHandler<AlarmEvent>? AlarmRaised;

    /// <param name="count">만들 위성 수</param>
    public FakeSatelliteService(int count = 10)
    {
        for (int i = 1; i <= count; i++)
        {
            _satellites.Add(new SatelliteStatus
            {
                Id = $"SAT-{i:0000}",                          // DB 더미 데이터와 같은 4자리 형식
                Battery = 50 + _random.NextDouble() * 50,      // 50~100%
                Temperature = _random.NextDouble() * 40 - 10   // -10~30°C
            });
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateValues();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    public Task SetModeAsync(string satelliteId, string mode)
    {
        var sat = _satellites.FirstOrDefault(s => s.Id == satelliteId);
        if (sat is not null)
            sat.Mode = mode;

        // 가짜라 바로 끝났다고 알림 (진짜 서버는 응답을 기다림)
        return Task.CompletedTask;
    }

    /// <summary>1초마다 호출: 모든 위성의 값을 조금씩 바꾼다</summary>
    private void UpdateValues()
    {
        foreach (var sat in _satellites)
        {
            // 알람은 "넘어가는 순간"에만 울리게 하려고, 바꾸기 전 상태를 기억
            bool wasLow = sat.Battery < SatelliteStatus.LowBatteryThreshold;

            sat.Battery = Math.Clamp(sat.Battery + (_random.NextDouble() * 2 - 1), 0, 100);
            sat.Temperature += _random.NextDouble() - 0.5;

            // 1% 확률로 온도 급상승 이상
            if (_random.NextDouble() < 0.01)
            {
                sat.Temperature += 15;
                RaiseAlarm(sat, $"온도 급상승: {sat.Temperature:F1}°C");
            }

            // 배터리가 기준 아래로 "떨어지는 순간" → SAFE 진입 + 알람
            if (!wasLow && sat.Battery < SatelliteStatus.LowBatteryThreshold)
            {
                sat.Mode = SatelliteMode.Safe;
                RaiseAlarm(sat, $"배터리 부족: {sat.Battery:F1}% → SAFE 모드 진입");
            }

            // 5% 확률로 교신 상태 전환
            if (_random.NextDouble() < 0.05)
                sat.InContact = !sat.InContact;
        }

        // 전체 갱신이 끝났다고 알림 (DB 기록기, 화면이 이걸 듣고 움직임)
        Updated?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseAlarm(SatelliteStatus sat, string message)
        => AlarmRaised?.Invoke(this, new AlarmEvent(DateTime.Now, sat.Id, message));
}
