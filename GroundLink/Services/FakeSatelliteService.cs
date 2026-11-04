using System.Windows.Threading;
using GroundLink.Models;

namespace GroundLink.Services;

/// <summary>
/// 서버 없이 화면을 개발하기 위한 가짜 위성 데이터 서비스.
/// 위성 10대를 만들고 1초마다 값을 조금씩 바꾼다.
/// 나중에 실제 서버 API를 부르는 서비스로 교체할 예정.
/// </summary>
public class FakeSatelliteService : ISatelliteService
{
    // 값 변화를 만들 난수 생성기
    private readonly Random _random = new();

    // 1초마다 Tick 이벤트를 발생시키는 타이머
    // DispatcherTimer는 화면(UI) 스레드에서 실행돼서, 화면에 묶인 값을 바꿔도 안전함
    private readonly DispatcherTimer _timer;

    // 실제 위성 목록 (내부에서 수정 가능한 List)
    private readonly List<SatelliteStatus> _satellites = new();

    // 외부에는 읽기 전용으로만 공개
    public IReadOnlyList<SatelliteStatus> Satellites => _satellites;

    public FakeSatelliteService()
    {
        // 위성 10대 초기값 생성 (SAT-001 ~ SAT-010)
        for (int i = 1; i <= 10; i++)
        {
            _satellites.Add(new SatelliteStatus
            {
                Id = $"SAT-{i:000}",                       // 3자리 숫자로 포맷
                Battery = 50 + _random.NextDouble() * 50,  // 50~100%
                Temperature = _random.NextDouble() * 40 - 10 // -10~30°C
            });
        }

        // 타이머 설정: 1초 간격, 매번 UpdateValues 실행
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateValues();
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public Task SetModeAsync(string satelliteId, string mode)
    {
        // ID가 같은 위성을 찾아서 모드 변경
        var sat = _satellites.FirstOrDefault(s => s.Id == satelliteId);
        if (sat is not null)
            sat.Mode = mode;

        // 가짜라 바로 끝났다고 알림 (진짜는 서버 응답을 기다림)
        return Task.CompletedTask;
    }

    // 위성마다 값을 조금씩 랜덤하게 변경
    private void UpdateValues()
    {
        foreach (var sat in _satellites)
        {
            // 배터리: -1 ~ +1 변화, 0~100 범위로 제한
            sat.Battery = Math.Clamp(sat.Battery + (_random.NextDouble() * 2 - 1), 0, 100);

            // 온도: -0.5 ~ +0.5 변화
            sat.Temperature += _random.NextDouble() - 0.5;

            // 배터리 20% 미만이면 SAFE 모드
            if (sat.Battery < 20)
                sat.Mode = "SAFE";

            // 5% 확률로 교신 상태 전환
            if (_random.NextDouble() < 0.05)
                sat.InContact = !sat.InContact;
        }
    }
}