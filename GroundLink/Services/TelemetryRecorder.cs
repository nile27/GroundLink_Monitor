using System.Diagnostics;
using GroundLink.Data;
using GroundLink.Models;

namespace GroundLink.Services;

/// <summary>
/// 서비스가 갱신될 때마다 현재 값을 DB에 기록하고, 저장 시간을 측정한다.
/// </summary>
public class TelemetryRecorder
{
    private readonly ISatelliteService _service;
    private readonly ITelemetryRepository _repository;

    // 측정용
    private readonly List<long> _durations = new();
    private int _inFlight;     // 지금 진행 중인 저장 개수
    private int _maxInFlight;  // 동시에 진행된 최대 개수 (1보다 크면 1초 안에 못 끝냈다는 뜻)

    public TelemetryRecorder(ISatelliteService service, ITelemetryRepository repository)
    {
        _service = service;
        _repository = repository;

        _service.Updated += OnUpdated;
        _service.AlarmRaised += OnAlarmRaised;
    }

    private async void OnUpdated(object? sender, EventArgs e)
    {
        // 현재 값 복사
        var now = DateTime.Now;
        var records = _service.Satellites
            .Select(s => new TelemetryRecord(s.Id, now, s.Battery, s.Temperature, s.Mode, s.InContact))
            .ToList();

        _inFlight++;
        _maxInFlight = Math.Max(_maxInFlight, _inFlight);
        var sw = Stopwatch.StartNew();

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
            sw.Stop();
            _inFlight--;
        }

        // 10번 저장마다 평균·최대 기록
        _durations.Add(sw.ElapsedMilliseconds);
        if (_durations.Count == 10)
        {
            App.Log($"[DB] {records.Count}건 저장 / 평균 {_durations.Average():F0} ms / 최대 {_durations.Max()} ms / 동시 진행 최대 {_maxInFlight}");
            _durations.Clear();
            _maxInFlight = 0;
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