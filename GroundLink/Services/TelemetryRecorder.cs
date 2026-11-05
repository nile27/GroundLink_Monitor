using System.Diagnostics;
using GroundLink.Data;
using GroundLink.Models;

namespace GroundLink.Services;

/// <summary>
/// 서비스가 갱신될 때마다 현재 값을 DB에 기록한다.
/// 서비스와 ViewModel은 DB를 모르게 하기 위한 중간 연결 담당.
/// </summary>
public class TelemetryRecorder
{
    private readonly ISatelliteService _service;
    private readonly ITelemetryRepository _repository;

    public TelemetryRecorder(ISatelliteService service, ITelemetryRepository repository)
    {
        _service = service;
        _repository = repository;

        _service.Updated += OnUpdated;
        _service.AlarmRaised += OnAlarmRaised;
    }

    // async void: 이벤트 처리기는 반환형을 바꿀 수 없어서 예외적으로 허용되는 형태
    // 대신 안에서 예외를 꼭 잡아야 함 (안 잡으면 앱이 죽음)
    private async void OnUpdated(object? sender, EventArgs e)
    {
        // 현재 값을 먼저 복사해둠 (저장하는 동안 값이 바뀌어도 안전)
        var now = DateTime.Now;
        var records = _service.Satellites
            .Select(s => new TelemetryRecord(s.Id, now, s.Battery, s.Temperature, s.Mode, s.InContact))
            .ToList();

        try
        {
            await _repository.SaveTelemetryAsync(records);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DB] 텔레메트리 저장 실패: {ex.Message}");
        }
    }

    private async void OnAlarmRaised(object? sender, AlarmEvent alarm)
    {
        try
        {
            await _repository.SaveAlarmAsync(alarm);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DB] 알람 저장 실패: {ex.Message}");
        }
    }
}