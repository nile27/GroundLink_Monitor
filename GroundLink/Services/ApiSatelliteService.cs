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

    // 명령 결과(ack) 기다리기: 서버의 ack 타임아웃(기본 10초)보다 조금 길게
    private static readonly TimeSpan AckWaitLimit = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan AckPollInterval = TimeSpan.FromMilliseconds(100);

    // 명령 성공 후, 서버 텔레메트리가 새 모드를 보여줄 때까지 화면 값을 지키는 시간.
    // 시뮬레이터는 ack는 바로 보내지만 새 모드가 담긴 텔레메트리는 다음 주기(1초)에 보낸다.
    private static readonly TimeSpan ModeHoldAfterAck = TimeSpan.FromSeconds(5);

    // 서버 JSON은 camelCase (battery, latestTelemetry ...) → Web 기본값이 대소문자를 맞춰줌
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly List<SatelliteStatus> _satellites = new();
    private readonly Dictionary<string, SatelliteStatus> _byId = new();

    // 명령을 보낸 위성: 서버 값이 새 모드로 바뀌기 전까지 옛 모드로 덮어쓰지 않음 (위성 id → 기다리는 모드, 기한)
    private readonly Dictionary<string, (string Mode, DateTime Until)> _pendingModes = new();

    // DispatcherTimer + ConfigureAwait 없는 await:
    // 응답을 받은 뒤 UI 스레드로 돌아와서 값을 바꾸므로 화면 바인딩이 안전하다.
    private readonly DispatcherTimer _timer;

    private bool _polling;          // 이전 조회가 아직 안 끝났는지 (겹쳐서 요청하지 않으려고)
    private bool _serverOk = true;  // 실패 로그를 매초 쌓지 않고, 상태가 바뀔 때만 남기려고
    private long? _lastAlarmId;     // 이미 본 알람 중 가장 큰 id. null이면 아직 기준을 못 잡음

    public IReadOnlyList<SatelliteStatus> Satellites => _satellites;

    public event EventHandler? Updated;
    public event EventHandler<AlarmEvent>? AlarmRaised;

    /// <summary>실제 서버(ApiSettings.BaseUrl)에 연결</summary>
    public ApiSatelliteService()
        : this(new HttpClient
        {
            BaseAddress = new Uri(ApiSettings.BaseUrl),
            Timeout = ApiSettings.RequestTimeout
        })
    {
    }

    /// <summary>HttpClient를 직접 넣는 생성자 (테스트에서 가짜 응답을 넣을 때 사용)</summary>
    public ApiSatelliteService(HttpClient http)
    {
        _http = http;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public void Start()
    {
        _timer.Start();
        _ = RefreshAsync(); // 1초 기다리지 않고 바로 한 번
    }

    public void Stop() => _timer.Stop();

    public void Dispose()
    {
        _timer.Stop();
        _http.Dispose();
    }

    // ───────── 명령 ─────────

    /// <summary>
    /// 모드 변경 명령을 보내고, 위성이 처리한 결과(ack)까지 기다린다.
    /// 성공하면 바로 서버 값을 한 번 다시 읽는다.
    /// 실패(서버 오류, 위성 거부, 시간 초과)하면 예외를 던진다 → ViewModel이 화면 모드를 되돌림.
    /// </summary>
    public async Task SetModeAsync(string satelliteId, string mode)
    {
        // 결과를 기다리는 동안 1초 조회가 옛 모드로 덮어쓰지 않게 표시
        _pendingModes[satelliteId] = (mode, DateTime.Now + AckWaitLimit);

        try
        {
            var path = $"satellites/{Uri.EscapeDataString(satelliteId)}/commands";

            // 1) 명령 접수 (서버는 202 + PENDING 상태의 명령을 돌려줌)
            using var res = await _http.PostAsJsonAsync(path, new CommandRequest("SET_MODE", mode), JsonOptions);
            res.EnsureSuccessStatusCode();
            var sent = await res.Content.ReadFromJsonAsync<CommandDto>(JsonOptions)
                       ?? throw new InvalidOperationException("명령 응답이 비어 있음");

            // 2) 위성의 처리 결과(ack) 기다리기. 보통 수 ms 안에 옴
            var result = await WaitForAckAsync(path, sent.Id);
            if (result.Status != "SUCCESS")
                throw new InvalidOperationException($"{result.Status}: {result.ResultMessage ?? "응답 없음"}");

            // 3) 성공: 새 텔레메트리가 올 때까지 잠깐 화면 값을 지키고, 바로 한 번 다시 읽기
            _pendingModes[satelliteId] = (mode, DateTime.Now + ModeHoldAfterAck);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _pendingModes.Remove(satelliteId); // 실패했으니 서버 값을 그대로 따른다
            PerfLog.Write($"[API] 명령 실패 {satelliteId} → {mode}: {ex.Message}");
            throw;
        }
    }

    /// <summary>명령 이력을 짧은 간격으로 조회해서 PENDING이 끝날 때까지 기다린다</summary>
    private async Task<CommandDto> WaitForAckAsync(string path, string commandId)
    {
        var deadline = DateTime.Now + AckWaitLimit;
        while (true)
        {
            var list = await _http.GetFromJsonAsync<List<CommandDto>>($"{path}?limit=20", JsonOptions);
            var cmd = list?.FirstOrDefault(c => c.Id == commandId);
            if (cmd is not null && cmd.Status != "PENDING")
                return cmd;

            if (DateTime.Now > deadline)
                throw new TimeoutException("명령 결과(ack)를 받지 못함");

            await Task.Delay(AckPollInterval);
        }
    }

    // ───────── 주기 조회 ─────────

    /// <summary>
    /// 서버에서 위성 목록과 알람을 한 번 읽어 반영한다 (1초 타이머가 호출, 명령 직후에도 호출).
    /// 실패해도 예외를 밖으로 내보내지 않는다.
    /// </summary>
    public async Task RefreshAsync()
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
            sat.InContact = t.Rssi is not null; // 서버엔 교신 칸이 없음. RSSI가 있으면 교신 중

            if (ShouldKeepScreenMode(dto.Id, t.Mode))
                continue; // 명령 결과가 아직 텔레메트리에 안 실림 → 화면 모드 유지
            sat.Mode = t.Mode;
        }
    }

    /// <summary>
    /// 명령을 보낸 위성인데 서버 값이 아직 옛 모드면 true.
    /// 서버가 새 모드를 보여주거나 기한이 지나면 표시를 지우고 서버 값을 따른다.
    /// </summary>
    private bool ShouldKeepScreenMode(string satelliteId, string serverMode)
    {
        if (!_pendingModes.TryGetValue(satelliteId, out var pending))
            return false;

        if (serverMode == pending.Mode || DateTime.Now > pending.Until)
        {
            _pendingModes.Remove(satelliteId);
            return false;
        }
        return true;
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

    private record CommandDto(string Id, string Status, string? ResultMessage);
}
