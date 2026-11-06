using System.Diagnostics;
using GroundLink.Data;
using GroundLink.Diagnostics;
using GroundLink.Models;

namespace GroundLink.Services;

/// <summary>
/// 서비스가 갱신될 때마다 현재 값을 DB에 기록하는 연결 담당.
/// 덕분에 서비스와 ViewModel은 DB의 존재를 모른다.
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

    // async void: 이벤트 처리기는 반환형을 바꿀 수 없어서 예외적으로 허용.
    // 대신 안에서 예외를 꼭 잡아야 한다 (안 잡으면 앱이 죽음).
    private async void OnUpdated(object? sender, EventArgs e)
    {
        // 저장하는 동안 값이 바뀔 수 있으니, 지금 값을 먼저 복사해둔다
        var now = DateTime.Now;
        var records = _service.Satellites
            .Select(s => new TelemetryRecord(s.Id, now, s.Battery, s.Temperature, s.Mode, s.InContact))
            .ToList();

        // 개발용 저장 시간 측정 (Release 빌드에선 PerfLog 호출이 빠짐)
        long start = Stopwatch.GetTimestamp();
        PerfLog.DbSaveStarted();

        try
        {
            await _repository.SaveTelemetryAsync(records);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DB] 텔레메트리 저장 실패: {ex.Message}");
        }
        finally
        {
            PerfLog.DbSaveFinished(records.Count, start);
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
