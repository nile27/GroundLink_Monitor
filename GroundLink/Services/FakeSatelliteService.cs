using System.Windows.Threading;
using GroundLink.Models;

namespace GroundLink.Services;

/// <summary>
/// 서버 없이 화면 개발용 가짜 위성 서비스.
/// 위성 10대를 1초마다 갱신하고, 기록을 남기고, 알람을 발생시킨다.
/// </summary>
public class FakeSatelliteService : ISatelliteService
{
    private const int MaxHistory = 120; // 위성당 2분치 기록

    private readonly Random _random = new();
    private readonly DispatcherTimer _timer;
    private readonly List<SatelliteStatus> _satellites = new();
    private readonly Dictionary<string, List<TelemetryPoint>> _history = new();

    public IReadOnlyList<SatelliteStatus> Satellites => _satellites;

    public event EventHandler? Updated;
    public event EventHandler<AlarmEvent>? AlarmRaised;

    public FakeSatelliteService()
    {
        // 위성 10대 생성
        for (int i = 1; i <= 10; i++)
        {
            _satellites.Add(new SatelliteStatus
            {
                Id = $"SAT-{i:000}",
                Battery = 50 + _random.NextDouble() * 50,   // 50~100%
                Temperature = _random.NextDouble() * 40 - 10 // -10~30°C
            });
        }

        // 1초 타이머 (UI 스레드에서 실행)
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
        return Task.CompletedTask;
    }

    public IReadOnlyList<TelemetryPoint> GetHistory(string satelliteId)
        => _history.TryGetValue(satelliteId, out var list)
            ? list
            : Array.Empty<TelemetryPoint>();

    // 1초마다 호출
    private void UpdateValues()
    {
        var now = DateTime.Now;

        foreach (var sat in _satellites)
        {
            // 갱신 전 상태 기억 (알람은 "넘어가는 순간"에만)
            bool wasLow = sat.Battery < 20;

            // 값 변화
            sat.Battery = Math.Clamp(sat.Battery + (_random.NextDouble() * 2 - 1), 0, 100);
            sat.Temperature += _random.NextDouble() - 0.5;

            // 1% 확률 온도 급상승
            if (_random.NextDouble() < 0.01)
            {
                sat.Temperature += 15;
                Raise(sat, $"온도 급상승: {sat.Temperature:F1}°C");
            }

            // 배터리 20% 아래로 떨어지는 순간 → SAFE 진입 + 알람
            if (!wasLow && sat.Battery < 20)
            {
                sat.Mode = "SAFE";
                Raise(sat, $"배터리 부족: {sat.Battery:F1}% → SAFE 모드 진입");
            }

            // 5% 확률 교신 상태 전환
            if (_random.NextDouble() < 0.05)
                sat.InContact = !sat.InContact;

            AddHistory(sat, now);
        }

        // 전체 갱신 끝 알림
        Updated?.Invoke(this, EventArgs.Empty);
    }

    // 기록 저장 (최대 120개 유지)
    private void AddHistory(SatelliteStatus sat, DateTime time)
    {
        if (!_history.TryGetValue(sat.Id, out var list))
        {
            list = new List<TelemetryPoint>();
            _history[sat.Id] = list;
        }

        list.Add(new TelemetryPoint(time, sat.Battery, sat.Temperature));
        if (list.Count > MaxHistory)
            list.RemoveAt(0);
    }

    // 알람 발생
    private void Raise(SatelliteStatus sat, string message)
        => AlarmRaised?.Invoke(this, new AlarmEvent(DateTime.Now, sat.Id, message));
}