using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Threading;
using GroundLink.Data;
using GroundLink.Diagnostics;
using GroundLink.Models;

namespace GroundLink.Services;

/// <summary>
/// 백엔드 서버 REST API로 위성 데이터를 가져오는 서비스.
/// FakeSatelliteService와 같은 약속(ISatelliteService)을 지키므로 ViewModel은 그대로 쓴다.
///
/// 1초마다 GET /satellites → 위성 값 갱신 → Updated 발생
/// 같은 주기로 GET /alarms → 새로 생긴 알람만 AlarmRaised 발생
/// 서버가 꺼져 있거나 실패하면 마지막 값을 그대로 두고 다음 주기에 다시 시도한다.
/// </summary>
public class ApiSatelliteService : ISatelliteService, IDisposable
{
    private const int AlarmFetchLimit = 50; // 한 번에 가져올 최근 알람 수

    // 서버 JSON은 camelCase (battery, latestTelemetry ...) → Web 기본값이 대소문자를 맞춰줌
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly List<SatelliteStatus> _satellites = new();
    private readonly Dictionary<string, SatelliteStatus> _byId = new();

    // DispatcherTimer + ConfigureAwait 없는 await:
    // 응답을 받은 뒤 UI 스레드로 돌아와서 값을 바꾸므로 화면 바인딩이 안전하다.
    private readonly DispatcherTimer _timer;

    private bool _polling;          // 이전 조회가 아직 안 끝났는지 (겹쳐서 요청하지 않으려고)
    private bool _serverOk = true;  // 실패 로그를 매초 쌓지 않고, 상태가 바뀔 때만 남기려고
    private long? _lastAlarmId;     // 이미 본 알람 중 가장 큰 id. null이면 아직 기준을 못 잡음

    public IReadOnlyList<SatelliteStatus> Satellites => _satellites;

    public event EventHandler? Updated;
    public event EventHandler<AlarmEvent>? AlarmRaised;

    public ApiSatelliteService()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(ApiSettings.BaseUrl),
            Timeout = ApiSettings.RequestTimeout
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await PollAsync();
    }

    public void Start()
    {
        _timer.Start();
        _ = PollAsync(); // 1초 기다리지 않고 바로 한 번
    }

    public void Stop() => _timer.Stop();

    public void Dispose()
    {
        _timer.Stop();
        _http.Dispose();
    }

    /// <summary>
    /// 모드 변경 명령. 서버는 202(접수)만 돌려주고, 실제 모드는 위성이 처리한 뒤
    /// 다음 텔레메트리로 바뀌어 들어온다. 그래서 여기서 화면 값을 직접 바꾸지 않는다.
    /// </summary>
    public async Task SetModeAsync(string satelliteId, string mode)
    {
        try
        {
            var body = new CommandRequest("SET_MODE", mode);
            using var res = await _http.PostAsJsonAsync(
                $"satellites/{Uri.EscapeDataString(satelliteId)}/commands", body, JsonOptions);
            res.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            // 버튼 명령에서 예외가 새어 나가면 앱이 죽으므로 여기서 막는다
            PerfLog.Write($"[API] 명령 실패 {satelliteId} → {mode}: {ex.Message}");
        }
    }

    // ───────── 주기 조회 ─────────

    private async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;

        try
        {
            var list = await _http.GetFromJsonAsync<List<SatelliteDto>>("satellites", JsonOptions);
            if (list is not null)
                ApplySatellites(list);

            await PollAlarmsAsync();

            if (!_serverOk)
            {
                _serverOk = true;
                PerfLog.Write("[API] 서버 연결 복구");
            }

            // 갱신이 끝났다고 알림 (FakeSatelliteService와 같은 약속).
            // 실패한 주기에는 보내지 않는다 → 기록기가 같은 옛날 값을 반복 저장하지 않음
            Updated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // 서버 꺼짐, 시간 초과, JSON 형식 오류 등. 마지막 값은 그대로 둔다
            if (_serverOk)
            {
                _serverOk = false;
                PerfLog.Write($"[API] 서버 조회 실패 (마지막 값 유지, 1초마다 재시도): {ex.Message}");
            }
        }
        finally
        {
            _polling = false;
        }
    }

    /// <summary>서버 목록을 화면용 객체에 반영. 이미 있는 위성은 같은 객체의 값만 바꾼다</summary>
    private void ApplySatellites(List<SatelliteDto> list)
    {
        foreach (var dto in list)
        {
            if (!_byId.TryGetValue(dto.Id, out var sat))
            {
                // 처음 보는 위성은 목록 끝에 추가 (ViewModel이 끝에서부터 새 위성을 가져감)
                sat = new SatelliteStatus { Id = dto.Id };
                _byId[dto.Id] = sat;
                _satellites.Add(sat);
            }

            var t = dto.LatestTelemetry;
            if (t is null) continue; // 아직 텔레메트리가 없는 위성은 기본값 유지

            sat.Battery = t.Battery;
            sat.Temperature = t.Temp;
            sat.Mode = t.Mode;
            sat.InContact = t.Rssi is not null; // 서버엔 교신 칸이 없음. RSSI가 있으면 교신 중
        }
    }

    /// <summary>
    /// 최근 알람을 가져와 새로 생긴 것만 발생시킨다.
    /// 첫 조회는 기준(가장 큰 id)만 잡고 발생시키지 않는다 → 앱을 켤 때 옛날 알람이 쏟아지지 않음
    /// </summary>
    private async Task PollAlarmsAsync()
    {
        var alarms = await _http.GetFromJsonAsync<List<AlarmDto>>($"alarms?limit={AlarmFetchLimit}", JsonOptions);
        if (alarms is null || alarms.Count == 0)
        {
            _lastAlarmId ??= 0;
            return;
        }

        // 서버는 최신순. id(bigint)는 문자열로 온다
        long maxId = alarms.Max(a => long.Parse(a.Id));

        if (_lastAlarmId is null)
        {
            _lastAlarmId = maxId;
            return;
        }

        // 오래된 것부터 발생시켜야 ViewModel이 맨 위에 넣을 때 최신이 맨 위에 온다
        foreach (var a in alarms.Where(a => long.Parse(a.Id) > _lastAlarmId).OrderBy(a => long.Parse(a.Id)))
        {
            AlarmRaised?.Invoke(this, new AlarmEvent(a.Ts.ToLocalTime(), a.SatId, a.Message));
        }

        _lastAlarmId = Math.Max(_lastAlarmId.Value, maxId);
    }

    // ───────── 서버 응답 모양 (필요한 칸만) ─────────

    private record SatelliteDto(string Id, TelemetryDto? LatestTelemetry);

    private record TelemetryDto(double Battery, double Temp, double? Rssi, string Mode);

    private record AlarmDto(string Id, string SatId, DateTime Ts, string Message);

    private record CommandRequest(string Type, string Mode);
}
