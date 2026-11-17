using System.Collections.Concurrent;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GroundLink.Data;
using GroundLink.Models;
using GroundLink.Network;
using GroundLink.Performance;
using GroundLink.Rules;

namespace GroundLink.ViewModels;

/*
 * 메인 화면에 보일 값 (ViewModel).
 * 화면 컨트롤을 모름: 값만 가지고, 바뀌면 PropertyChanged로 알림 → WPF 바인딩이 화면을 갱신.
 * 색은 WPF Brush 대신 "#RRGGBB" 글자로 가짐: WPF 타입이 없어야 화면 없이 테스트 가능.
 *
 * [ObservableProperty]: 필드 하나로 속성 + 변경 알림 코드를 빌드 때 자동 생성 (CommunityToolkit.Mvvm)
 *   private string connectionText → public string ConnectionText { get; set; } + PropertyChanged
 *   생성 코드를 합치려면 클래스가 partial이어야 함
 *
 * [RelayCommand]: 메서드 하나로 버튼에 바인딩할 Command 속성을 자동 생성
 *   private void LoadLatest() → public IRelayCommand LoadLatestCommand
 *
 * 1. 연결 상태: 상태 글자, 색, 상세 글자
 * 2. 현재 값: 배터리·배터리 온도 숫자, 경고까지 남은 여유, 경고 시 색
 * 3. 오류 글자
 * 4. 과거 기록: 선택된 기간, 기간 설명, 결론 한 줄
 * 5. 명령: DB 최신 값 조회, 시뮬레이터 연결, 과거 기록 기간 선택
 * 6. 수신 처리: 상태 표시, 10건씩 DB 저장, 끊기면 남은 것 저장
 * 7. 과거 기록 조회·결론 (차트는 HistoryLoaded로 화면에 알림)
 * 8. 성능 측정: 수신 건수, DB 저장 시간, 수신→화면 지연, 차트 그리기 시간 (1초마다 표시)
 *
 * 스레드
 *   수신 이벤트는 수신 스레드에서 옴. DB 저장은 그 스레드에서 (UI가 멈추지 않게)
 *   화면에 보일 값 변경은 runOnUi로 UI 스레드에 넘김 → 값은 항상 UI 스레드에서만 바뀜
 *   runOnUi: 창은 Dispatcher.InvokeAsync, 테스트는 바로 실행 → ViewModel은 WPF를 모름
 *
 * 화면 갱신 묶기 (BatchUiUpdates)
 *   끔: 1건 받을 때마다 UI 스레드에 넘겨 화면·차트 갱신 (1초 100건이면 초당 100번)
 *   켬: 받은 건은 큐에 쌓고, 화면이 0.1초마다 FlushPending()으로 몰아서 1번 갱신 (초당 최대 10번)
 */
public partial class MainViewModel : ObservableObject
{
    // 과거 기록 기간 (XAML 버튼의 CommandParameter와 같은 값)
    public const string PeriodRecent = "3h";
    public const string PeriodDay = "1d";
    public const string PeriodWeek = "1w";

    private const int SaveBatchSize = 10;   // 10건 모이면 저장 (1초 1건 → 약 10초마다)

    private readonly TelemetryRepository repository;
    private readonly string satelliteId;
    private readonly string simulatorHost;
    private readonly int simulatorPort;
    private readonly Action<Action> runOnUi;

    // 저장 대기 리스트 (수신 스레드에서만 사용)
    private readonly List<Telemetry> buffer = new();

    // 화면 반영 대기 큐: 수신 스레드가 넣고 UI 스레드가 꺼냄 → 여러 스레드용 큐 (ConcurrentQueue)
    // Stamp: 받은 순간의 Stopwatch 값 (화면 반영까지 걸린 시간 측정용)
    private readonly ConcurrentQueue<(Telemetry Data, long Stamp)> pending = new();

    private readonly PerfMonitor perf = new();
    private int receiveThreadId;   // 수신 스레드 번호 (확인용 표시)

    private bool isStarted;   // 연결 버튼을 한 번 누르면 true. 이후 재접속은 수신기가 알아서 함

    /// <summary>실시간 차트용: 수신 건 묶음 (UI 스레드에서 발생). 차트는 바인딩이 안 돼서 화면이 직접 그림.</summary>
    public event Action<IReadOnlyList<Telemetry>>? TelemetryReceived;

    /// <summary>과거 기록 차트용: 조회 완료 (UI 스레드에서 발생).</summary>
    public event Action<HistoryChartData>? HistoryLoaded;

    /// <summary>필요한 것을 만드는 쪽(창)에서 받음. 테스트 때 다른 값으로 바꿔 넣기 쉬움.</summary>
    public MainViewModel(
        TelemetryRepository repository, string satelliteId,
        string simulatorHost, int simulatorPort, Action<Action> runOnUi)
    {
        this.repository = repository;
        this.satelliteId = satelliteId;
        this.simulatorHost = simulatorHost;
        this.simulatorPort = simulatorPort;
        this.runOnUi = runOnUi;
    }

    // ───────── 1. 연결 상태 ─────────

    [ObservableProperty] private string connectionText = "연결 안 됨";   // 연결 안 됨 / 접속 중 / 수신 중 / 끊김
    [ObservableProperty] private string connectionColor = Theme.Muted;   // 상태 점·글자 색
    [ObservableProperty] private string connectionDetail = "";           // 마지막 수신 시각, 끊긴 이유 등

    // ───────── 2. 현재 값 ─────────

    [ObservableProperty] private string batteryText = "-";                         // "96.1 %"
    [ObservableProperty] private string batteryColor = Theme.Text;
    [ObservableProperty] private string batteryMarginText = "경고 기준 60 % 미만";   // "경고까지 36.1 %p"
    [ObservableProperty] private string batteryMarginColor = Theme.Muted;

    [ObservableProperty] private string temperatureText = "-";
    [ObservableProperty] private string temperatureColor = Theme.Text;
    [ObservableProperty] private string temperatureMarginText = "경고 기준 0 °C 미만 · 40 °C 초과";
    [ObservableProperty] private string temperatureMarginColor = Theme.Muted;

    // ───────── 3. 오류 ─────────

    [ObservableProperty] private string errorText = "";   // 조회·저장 실패 메시지 (빨간 줄)

    // ───────── 4. 과거 기록 ─────────

    // 선택된 기간이 바뀌면 버튼 강조용 Is…Selected 3개도 바뀌었다고 같이 알림
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecentSelected))]
    [NotifyPropertyChangedFor(nameof(IsDaySelected))]
    [NotifyPropertyChangedFor(nameof(IsWeekSelected))]
    private string selectedPeriod = "";

    public bool IsRecentSelected => SelectedPeriod == PeriodRecent;
    public bool IsDaySelected => SelectedPeriod == PeriodDay;
    public bool IsWeekSelected => SelectedPeriod == PeriodWeek;

    [ObservableProperty] private string historyDescription = "";                       // 기간 설명 (차트 읽는 법)
    [ObservableProperty] private string batteryHistorySummary = "배터리: -";           // 결론 한 줄
    [ObservableProperty] private string batteryHistoryColor = Theme.Text;
    [ObservableProperty] private string temperatureHistorySummary = "배터리 온도: -";
    [ObservableProperty] private string temperatureHistoryColor = Theme.Text;

    // ───────── 8. 성능 측정 (표시) ─────────

    [ObservableProperty] private bool batchUiUpdates = true;   // 화면 갱신 묶기 (체크박스)
    [ObservableProperty] private string perfText = "성능: 연결 후 1초마다 표시";

    // ───────── 5. 명령 ─────────

    /// <summary>
    /// DB 최신 값을 현재 값 카드에 표시. "최신 값 조회" 버튼과 창 시작 때 사용.
    /// [RelayCommand] → LoadLatestCommand 생성. XAML: Command="{Binding LoadLatestCommand}"
    /// </summary>
    [RelayCommand]
    private void LoadLatest()
    {
        try
        {
            Telemetry? data = repository.GetLatest(satelliteId);
            if (data == null)
            {
                ErrorText = "데이터 없음";
                return;
            }

            ShowCurrentValues(data);
            ConnectionDetail = $"DB 최신 값 {data.RecordedAt:yyyy-MM-dd HH:mm:ss}";
        }
        catch (Exception ex)
        {
            ErrorText = $"조회 실패: {ex.Message}";
        }
    }

    /// <summary>
    /// "시뮬레이터 연결": 수신 시작. 수신은 별도 스레드라 UI는 바로 자유.
    /// CanExecute = CanConnect: false가 되면 바인딩된 버튼이 자동으로 꺼짐 (IsEnabled를 직접 안 건드림).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private void Connect()
    {
        isStarted = true;
        ConnectCommand.NotifyCanExecuteChanged();   // "CanConnect 다시 물어봐" → 버튼 꺼짐

        var receiver = new TelemetryReceiver(simulatorHost, simulatorPort);
        receiver.Connecting += OnConnecting;
        receiver.Received += OnReceived;
        receiver.Disconnected += OnDisconnected;
        receiver.Start();
    }

    /// <summary>연결 버튼을 누를 수 있는지. 끊겨도 수신기가 재접속하므로 한 번 누르면 계속 false.</summary>
    private bool CanConnect() => !isStarted;

    /// <summary>
    /// 과거 기록 기간 버튼. period = PeriodRecent / PeriodDay / PeriodWeek (XAML CommandParameter).
    /// 3시간·1일은 분 단위 평균 파동, 1주일은 파동이 약 107개라 화면 폭에서 띠로 뭉개지므로 궤도 1바퀴 단위.
    /// </summary>
    [RelayCommand]
    private void ShowHistory(string period)
    {
        SelectedPeriod = period;

        try
        {
            switch (period)
            {
                case PeriodRecent: LoadWaveHistory(3, 1, "3시간"); break;
                case PeriodDay: LoadWaveHistory(24, 5, "1일"); break;
                case PeriodWeek: LoadOrbitHistory(168, "1주일"); break;
            }
        }
        catch (Exception ex)
        {
            ErrorText = $"차트 조회 실패: {ex.Message}";
        }
    }

    // ───────── 6. 수신 처리 (On… = 수신 스레드, 나머지 = UI 스레드) ─────────

    /// <summary>접속 시도: 첫 시도는 "접속 중", 2번째부터는 몇 번째 재접속인지 표시.</summary>
    private void OnConnecting(int attempt)
    {
        runOnUi(() => SetConnection("접속 중", Theme.Caution,
            attempt == 1 ? "" : $"재접속 시도 {attempt}번째"));
    }

    /// <summary>
    /// 1건 수신: 저장 대기 리스트에 넣고(10건이면 저장), 화면 반영은 묶기 설정에 따라
    /// 큐에 쌓거나(FlushPending이 꺼냄) 바로 UI 스레드로 넘김.
    /// </summary>
    private void OnReceived(Telemetry data)
    {
        long stamp = Stopwatch.GetTimestamp();
        receiveThreadId = Environment.CurrentManagedThreadId;
        perf.AddReceived();

        buffer.Add(data);
        if (buffer.Count >= SaveBatchSize)
        {
            // DB 오류로 수신까지 끊기지 않게 여기서 처리. 실패하면 비우지 않고 다음 건 때 다시 저장 시도
            try
            {
                var watch = Stopwatch.StartNew();
                repository.InsertBatch(buffer);
                perf.AddSave(watch.Elapsed);
                buffer.Clear();   // 저장한 뒤 비우기
            }
            catch (Exception ex)
            {
                string message = $"저장 실패 (대기 {buffer.Count}건): {ex.Message}";
                runOnUi(() => ErrorText = message);
            }
        }

        if (BatchUiUpdates)
            pending.Enqueue((data, stamp));
        else
            runOnUi(() => ApplyReceived(new[] { (data, stamp) }));
    }

    /// <summary>화면 갱신 묶기: 큐에 쌓인 수신 건을 한 번에 화면에 반영. 화면이 0.1초마다 부름 (UI 스레드).</summary>
    public void FlushPending()
    {
        var items = new List<(Telemetry Data, long Stamp)>();
        while (pending.TryDequeue(out var item))
        {
            items.Add(item);
        }

        if (items.Count > 0) ApplyReceived(items);
    }

    /// <summary>수신 건 반영 (UI 스레드): 상태·현재 값은 마지막 건으로, 차트에는 전부 넘김.</summary>
    private void ApplyReceived(IReadOnlyList<(Telemetry Data, long Stamp)> items)
    {
        foreach (var item in items)
        {
            perf.AddUiLag(Stopwatch.GetElapsedTime(item.Stamp));   // 받은 순간 → 지금
        }

        Telemetry last = items[^1].Data;
        SetConnection("수신 중", Theme.Ok,
            $"마지막 수신 {last.RecordedAt:HH:mm:ss.fff} (수신 스레드 {receiveThreadId})");
        ShowCurrentValues(last);
        TelemetryReceived?.Invoke(items.Select(i => i.Data).ToList());
    }

    /// <summary>차트 1번 그린 시간 기록. 화면이 ScottPlot RenderFinished에서 부름 (UI 스레드).</summary>
    public void ReportRender(TimeSpan elapsed) => perf.AddRender(elapsed);

    /// <summary>지난 1초 측정값을 성능 글자로 표시. 화면이 1초마다 부름 (UI 스레드).</summary>
    public void PublishPerf()
    {
        PerfSnapshot p = perf.Snapshot();
        PerfText =
            $"수신 {p.Received}건/초 · 차트 그리기 {p.Renders}회/초, 평균 {p.RenderMs:F1} ms (최대 {p.RenderMaxMs:F1}) · " +
            $"수신→화면 평균 {p.UiLagMs:F0} ms (최대 {p.UiLagMaxMs:F0}) · DB 저장 1회 평균 {p.SaveMs:F1} ms";
    }

    /// <summary>끊김·접속 실패: 남은 데이터 저장, 사유와 재접속 예정 표시.</summary>
    private void OnDisconnected(string reason)
    {
        try
        {
            if (buffer.Count > 0)
            {
                repository.InsertBatch(buffer);
                buffer.Clear();
            }
        }
        catch (Exception ex)
        {
            reason += $" / 남은 데이터 저장 실패: {ex.Message}";
        }

        runOnUi(() => SetConnection("끊김", Theme.Danger,
            $"{reason} · {TelemetryReceiver.RetryDelaySeconds}초 후 재접속"));   // 저장 실패 시 그 내용 포함
    }

    // ───────── 7. 과거 기록 조회·결론 ─────────

    /// <summary>분 단위 평균 파동: 조회 → 설명·결론 → 차트에 알림.</summary>
    private void LoadWaveHistory(int hours, int bucketMinutes, string label)
    {
        HistoryDescription =
            $"{bucketMinutes}분 평균 · 궤도 1바퀴(94.5분)마다 파동 1개. " +
            "햇빛에서 충전되어 오르고(↗), 그늘에서 방전되어 내려감(↘). 온도도 같은 방향";

        HistorySummary summary = repository.GetSummary(satelliteId, hours, bucketMinutes);
        SummarizeBatteryWave(summary.Battery);
        SummarizeTemperatureWave(summary.Temperature);
        HistoryLoaded?.Invoke(new HistoryChartData(summary, label, ByOrbit: false));
    }

    /// <summary>궤도 1바퀴 단위: 조회 → 설명·결론 → 차트에 알림.</summary>
    private void LoadOrbitHistory(int hours, string label)
    {
        HistoryDescription =
            "궤도 1바퀴(94.5분) = 점 1개 · 위 선 = 바퀴 중 최고(햇빛 끝), 아래 선 = 바퀴 중 최저(그늘 끝). " +
            "선이 평평하면 매 바퀴 같은 폭으로 충전·방전 중, 아래 선이 내려가면 방전이 깊어진 것. 다 끝난 바퀴만 표시";

        HistorySummary summary = repository.GetOrbitSummary(satelliteId, hours);
        SummarizeBatteryOrbit(summary.Battery);
        SummarizeTemperatureOrbit(summary.Temperature);
        HistoryLoaded?.Invoke(new HistoryChartData(summary, label, ByOrbit: true));
    }

    /// <summary>파동 차트 배터리 결론: 경고 구간 수, 최저값, 경고선까지 여유.</summary>
    private void SummarizeBatteryWave(List<SummaryPoint> points)
    {
        if (points.Count == 0)
        {
            SetBatterySummary("배터리: 데이터 없음", Theme.Muted);
            return;
        }

        int warnCount = points.Count(p => WarningRules.IsBatteryWarning(p.Min));   // 경고선 아래로 내려간 구간 수
        double lowest = points.Min(p => p.Min);

        if (warnCount == 0)
            SetBatterySummary(
                $"배터리: 경고 없음 · 최저 {lowest:F1} % (경고까지 {WarningRules.BatteryMargin(lowest):F1} %p)", Theme.Ok);
        else
            SetBatterySummary($"배터리: 경고 구간 {warnCount}개 · 최저 {lowest:F1} %", Theme.Danger);
    }

    /// <summary>파동 차트 온도 결론: 경고 구간 수, 최저~최고, 가까운 경고선까지 여유.</summary>
    private void SummarizeTemperatureWave(List<SummaryPoint> points)
    {
        if (points.Count == 0)
        {
            SetTemperatureSummary("배터리 온도: 데이터 없음", Theme.Muted);
            return;
        }

        // 구간 안에서 한 번이라도 범위를 벗어났으면 경고 구간
        int warnCount = points.Count(p =>
            WarningRules.IsTemperatureWarning(p.Min) || WarningRules.IsTemperatureWarning(p.Max));
        double lowest = points.Min(p => p.Min);
        double highest = points.Max(p => p.Max);
        double margin = Math.Min(WarningRules.TemperatureMargin(lowest), WarningRules.TemperatureMargin(highest));

        if (warnCount == 0)
            SetTemperatureSummary(
                $"배터리 온도: 경고 없음 · {lowest:F1} ~ {highest:F1} °C (경고까지 {margin:F1} °C)", Theme.Ok);
        else
            SetTemperatureSummary($"배터리 온도: 경고 구간 {warnCount}개 · {lowest:F1} ~ {highest:F1} °C", Theme.Danger);
    }

    /// <summary>바퀴별 배터리 결론: 바퀴 수, 가장 낮은 최저값, 첫 바퀴→마지막 바퀴 최저값 변화.</summary>
    private void SummarizeBatteryOrbit(List<SummaryPoint> points)
    {
        if (points.Count == 0)
        {
            SetBatterySummary("배터리: 다 끝난 바퀴 없음 (데이터 부족)", Theme.Muted);
            return;
        }

        int warnCount = points.Count(p => WarningRules.IsBatteryWarning(p.Min));   // 경고선 아래로 내려간 바퀴 수
        double lowest = points.Min(p => p.Min);
        double change = points[^1].Min - points[0].Min;   // 음수가 커지면 방전이 깊어지는 중

        if (warnCount == 0)
            SetBatterySummary(
                $"배터리: {points.Count}바퀴 경고 없음 · 최저 {lowest:F1} % (경고까지 {WarningRules.BatteryMargin(lowest):F1} %p) · " +
                $"최저 변화 {change:+0.0;-0.0;0.0} %p", Theme.Ok);
        else
            SetBatterySummary($"배터리: {points.Count}바퀴 중 경고 {warnCount}바퀴 · 최저 {lowest:F1} %", Theme.Danger);
    }

    /// <summary>바퀴별 온도 결론: 바퀴 수, 최저~최고, 가까운 경고선까지 여유.</summary>
    private void SummarizeTemperatureOrbit(List<SummaryPoint> points)
    {
        if (points.Count == 0)
        {
            SetTemperatureSummary("배터리 온도: 다 끝난 바퀴 없음 (데이터 부족)", Theme.Muted);
            return;
        }

        int warnCount = points.Count(p =>
            WarningRules.IsTemperatureWarning(p.Min) || WarningRules.IsTemperatureWarning(p.Max));
        double lowest = points.Min(p => p.Min);
        double highest = points.Max(p => p.Max);
        double margin = Math.Min(WarningRules.TemperatureMargin(lowest), WarningRules.TemperatureMargin(highest));

        if (warnCount == 0)
            SetTemperatureSummary(
                $"배터리 온도: {points.Count}바퀴 경고 없음 · {lowest:F1} ~ {highest:F1} °C (경고까지 {margin:F1} °C)", Theme.Ok);
        else
            SetTemperatureSummary(
                $"배터리 온도: {points.Count}바퀴 중 경고 {warnCount}바퀴 · {lowest:F1} ~ {highest:F1} °C", Theme.Danger);
    }

    /// <summary>배터리 결론 글자와 색.</summary>
    private void SetBatterySummary(string text, string colorHex)
    {
        BatteryHistorySummary = text;
        BatteryHistoryColor = colorHex;
    }

    /// <summary>온도 결론 글자와 색.</summary>
    private void SetTemperatureSummary(string text, string colorHex)
    {
        TemperatureHistorySummary = text;
        TemperatureHistoryColor = colorHex;
    }

    // ───────── 공통 ─────────

    /// <summary>연결 상태 3개를 한 번에 바꿈.</summary>
    public void SetConnection(string text, string colorHex, string detail)
    {
        ConnectionText = text;
        ConnectionColor = colorHex;
        ConnectionDetail = detail;
    }

    /// <summary>현재 값 카드: 숫자 + 경고까지 남은 여유. 경고면 빨간색.</summary>
    public void ShowCurrentValues(Telemetry data)
    {
        bool batteryWarn = WarningRules.IsBatteryWarning(data.Battery);
        BatteryText = $"{data.Battery:F1} %";
        BatteryColor = batteryWarn ? Theme.Danger : Theme.Text;
        BatteryMarginText = batteryWarn
            ? $"경고: {WarningRules.BatteryWarning} % 미만"
            : $"경고까지 {WarningRules.BatteryMargin(data.Battery):F1} %p";
        BatteryMarginColor = batteryWarn ? Theme.Danger : Theme.Muted;

        bool tempWarn = WarningRules.IsTemperatureWarning(data.Temperature);
        TemperatureText = $"{data.Temperature:F1} °C";
        TemperatureColor = tempWarn ? Theme.Danger : Theme.Text;
        TemperatureMarginText = tempWarn
            ? $"경고: {WarningRules.TemperatureLow} ~ {WarningRules.TemperatureHigh} °C 벗어남"
            : $"경고까지 {WarningRules.TemperatureMargin(data.Temperature):F1} °C";
        TemperatureMarginColor = tempWarn ? Theme.Danger : Theme.Muted;
    }
}

/// <summary>
/// 과거 기록 차트에 넘길 데이터.
/// Summary: 조회 결과 / Label: "1일" 등 제목용 / ByOrbit: true면 궤도 1바퀴 단위 차트, false면 평균 파동 차트
/// </summary>
public record HistoryChartData(HistorySummary Summary, string Label, bool ByOrbit);
