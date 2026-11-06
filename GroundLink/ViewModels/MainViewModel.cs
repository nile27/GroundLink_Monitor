using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GroundLink.Data;
using GroundLink.Diagnostics;
using GroundLink.Models;
using GroundLink.Services;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace GroundLink.ViewModels;

/// <summary>
/// 메인 화면 ViewModel.
/// 위성 목록, 선택 위성, 배터리 차트(DB 조회), 알람 목록, 모드 전환 명령을 담당한다.
/// 화면(XAML)은 여기 있는 속성과 명령에 바인딩만 한다.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    // ═════════ ① 설정값 ═════════

    private const int MaxAlarms = 100;          // 알람 표에 남길 최대 개수
    private const int ChartTargetPoints = 480;  // 차트를 몇 칸으로 요약할지 (화면 폭 기준)
    private const int ChartRefreshTicks = 5;    // 몇 초마다 차트를 새로 불러올지

    private static readonly PeriodOption[] PeriodOptions =
    {
        new("최근 1분", 1),
        new("최근 1시간", 60),
        new("최근 24시간", 1440),
    };

    // ═════════ ② 필드 (내부에서만 씀) ═════════

    private readonly ISatelliteService _service;
    private readonly ITelemetryRepository _repository;

    private readonly LineSeries _avgSeries;   // 차트: 평균 선
    private readonly AreaSeries _bandSeries;  // 차트: 최소~최대 띠

    private int _tick;             // 1초 갱신 횟수 (차트 새로고침 주기 계산용)
    private bool _loading;         // 차트 불러오는 중인지
    private bool _reloadPending;   // 불러오는 중에 또 요청이 왔는지

    // ═════════ ③ 화면이 바인딩하는 속성 ═════════

    /// <summary>위성 카드 목록</summary>
    public ObservableCollection<SatelliteStatus> Satellites { get; }

    /// <summary>알람 표 (최신이 맨 위)</summary>
    public ObservableCollection<AlarmEvent> Alarms { get; } = new();

    /// <summary>배터리 차트</summary>
    public PlotModel BatteryPlot { get; }

    /// <summary>기간 선택 콤보박스 항목</summary>
    public IReadOnlyList<PeriodOption> Periods => PeriodOptions;

    /// <summary>선택된 위성 (없으면 null)</summary>
    [ObservableProperty]
    private SatelliteStatus? selectedSatellite;

    /// <summary>선택된 기간 (기본: 최근 1시간)</summary>
    [ObservableProperty]
    private PeriodOption selectedPeriod = PeriodOptions[1];

    /// <summary>차트 아래 안내 문구 (예: 480점 / 조회 150 ms)</summary>
    [ObservableProperty]
    private string chartInfo = "";

    // 상단 요약 바 (1초마다 UpdateSummary()로 갱신)

    /// <summary>전체 위성 수</summary>
    [ObservableProperty]
    private int totalCount;

    /// <summary>경고 위성 수 (SAFE 또는 배터리 부족)</summary>
    [ObservableProperty]
    private int warningCount;

    /// <summary>SAFE 모드 위성 수. 바뀌면 "SAFE 전체 복귀" 버튼 활성 여부도 다시 계산</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreAllSafeCommand))]
    private int safeCount;

    /// <summary>교신 중인 위성 수</summary>
    [ObservableProperty]
    private int contactCount;

    // ═════════ ④ 생성자 ═════════

    /// <summary>
    /// DI 컨테이너가 서비스와 저장소를 넣어준다 (생성자 주입).
    /// ViewModel은 그게 가짜인지 진짜인지 모른다.
    /// </summary>
    public MainViewModel(ISatelliteService service, ITelemetryRepository repository)
    {
        _service = service;
        _repository = repository;

        Satellites = new ObservableCollection<SatelliteStatus>(_service.Satellites);

        _avgSeries = new LineSeries { Title = "평균", Color = OxyColors.SteelBlue };
        _bandSeries = new AreaSeries
        {
            Title = "최소~최대",
            Color = OxyColor.FromAColor(80, OxyColors.SteelBlue),
            Fill = OxyColor.FromAColor(40, OxyColors.SteelBlue),
            StrokeThickness = 0
        };
        BatteryPlot = CreateBatteryPlot();

        // 서비스 이벤트 구독
        _service.Updated += OnServiceUpdated;    // 1초마다 → 주기적으로 차트 새로고침
        _service.AlarmRaised += OnAlarmRaised;   // 알람 → 표에 추가

        UpdateSummary(); // 첫 갱신 전에도 요약 바가 비어 보이지 않게
        _service.Start();
    }

    // ═════════ ⑤ 명령 (버튼) ═════════

    /// <summary>
    /// 모드 전환 버튼. [RelayCommand]가 ToggleSafeModeCommand를 자동 생성한다.
    /// CanExecute가 false면(선택 없음) 버튼이 자동으로 비활성화된다.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanToggleSafeMode))]
    private async Task ToggleSafeModeAsync(SatelliteStatus? satellite)
    {
        if (satellite is null) return;

        var next = satellite.Mode == SatelliteMode.Safe ? SatelliteMode.Normal : SatelliteMode.Safe;
        await ChangeModeAsync(satellite, next);
    }

    private bool CanToggleSafeMode(SatelliteStatus? satellite) => satellite is not null;

    /// <summary>
    /// "SAFE 전체 복귀" 버튼: SAFE인 위성들에만 NORMAL 명령을 동시에 보낸다.
    /// 배터리 부족 위성은 위성이 거부할 수 있고, 그 위성만 SAFE로 되돌아간다.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRestoreAllSafe))]
    private async Task RestoreAllSafeAsync()
    {
        var targets = Satellites.Where(s => s.Mode == SatelliteMode.Safe).ToList();
        await Task.WhenAll(targets.Select(s => ChangeModeAsync(s, SatelliteMode.Normal)));
    }

    private bool CanRestoreAllSafe() => SafeCount > 0;

    /// <summary>
    /// 모드 변경 (낙관적 업데이트).
    /// 서버 응답을 기다리지 않고 화면 모드를 먼저 바꾸고, 명령이 실패하면 원래 모드로 되돌린다.
    /// </summary>
    private async Task ChangeModeAsync(SatelliteStatus satellite, string mode)
    {
        var previous = satellite.Mode;

        satellite.Mode = mode; // ① 화면 먼저
        UpdateSummary();       //    요약 바도 바로

        try
        {
            await _service.SetModeAsync(satellite.Id, mode); // ② 실제 명령
        }
        catch (Exception ex)
        {
            satellite.Mode = previous; // ③ 실패 → 되돌리기 + 알람 표에 기록
            UpdateSummary();
            AddAlarm(new AlarmEvent(DateTime.Now, satellite.Id, $"명령 실패: {mode} 전환 ({ex.Message})"));
            PerfLog.Write($"[명령] {satellite.Id} → {mode} 실패, {previous}로 되돌림: {ex.Message}");
        }
    }

    // ═════════ ⑥ 차트 ═════════

    private PlotModel CreateBatteryPlot()
    {
        var model = new PlotModel { Title = "배터리 (%)" };
        model.Axes.Add(new DateTimeAxis { Position = AxisPosition.Bottom, StringFormat = "MM-dd HH:mm" });
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Minimum = 0, Maximum = 100 });
        model.Series.Add(_bandSeries);
        model.Series.Add(_avgSeries);
        return model;
    }

    // [ObservableProperty]가 만들어주는 "값이 바뀐 직후" 훅
    partial void OnSelectedSatelliteChanged(SatelliteStatus? value) => _ = ReloadChartAsync();
    partial void OnSelectedPeriodChanged(PeriodOption value) => _ = ReloadChartAsync();

    private void OnServiceUpdated(object? sender, EventArgs e)
    {
        AddNewSatellites();
        UpdateSummary(); // 요약 바는 매초 갱신

        if (++_tick % ChartRefreshTicks == 0)
            _ = ReloadChartAsync();
    }

    /// <summary>
    /// 서비스 목록에 새로 생긴 위성을 화면 목록에 추가.
    /// 서버 연결 시엔 처음에 비어 있다가 나중에 채워지기 때문.
    /// 서비스는 새 위성을 항상 목록 끝에 붙이므로, 화면 목록보다 늘어난 부분만 가져온다
    /// (위성 1만 대여도 매초 전체를 비교하지 않음).
    /// </summary>
    private void AddNewSatellites()
    {
        var source = _service.Satellites;
        for (int i = Satellites.Count; i < source.Count; i++)
            Satellites.Add(source[i]);
    }

    /// <summary>상단 요약 바 숫자를 현재 위성 상태로 다시 센다</summary>
    private void UpdateSummary()
    {
        TotalCount = Satellites.Count;
        WarningCount = Satellites.Count(s => s.IsWarning);
        SafeCount = Satellites.Count(s => s.Mode == SatelliteMode.Safe);
        ContactCount = Satellites.Count(s => s.InContact);
    }

    /// <summary>
    /// 선택된 위성의 기록을 DB에서 요약해서 가져와 차트를 다시 그린다.
    /// 기간을 약 480칸으로 묶어서 가져오므로, 24시간이어도 점은 약 480개.
    /// (측정: 24시간 원본 약 86,000점 조회 약 270ms / 요약 480점 약 150ms)
    /// </summary>
    private async Task ReloadChartAsync()
    {
        // 이미 불러오는 중이면, 끝난 뒤 한 번 더 하도록 표시만 하고 나감
        if (_loading) { _reloadPending = true; return; }
        _loading = true;

        try
        {
            var sat = SelectedSatellite;
            if (sat is null)
            {
                ClearChart();
                ChartInfo = "";
                return;
            }

            var to = DateTime.Now;
            var from = to.AddMinutes(-SelectedPeriod.Minutes);
            int bucketSeconds = Math.Max(1, SelectedPeriod.Minutes * 60 / ChartTargetPoints);

            var sw = Stopwatch.StartNew();
            var points = await _repository.GetBatteryHistoryAsync(sat.Id, from, to, bucketSeconds);
            long queryMs = sw.ElapsedMilliseconds;

            // 평균은 선, 최소~최대는 띠
            sw.Restart(); // 차트 채우기 시간 측정
            ClearChart();
            foreach (var p in points)
            {
                double x = DateTimeAxis.ToDouble(p.Time);
                _avgSeries.Points.Add(new DataPoint(x, p.Avg));
                _bandSeries.Points.Add(new DataPoint(x, p.Max));
                _bandSeries.Points2.Add(new DataPoint(x, p.Min));
            }
            BatteryPlot.InvalidatePlot(true); // OxyPlot은 점을 바꾼 뒤 직접 "다시 그려라" 신호를 보내야 함
            long fillMs = sw.ElapsedMilliseconds;

            ChartInfo = $"{points.Count:N0}점 / {bucketSeconds}초 단위 / 조회 {queryMs} ms";
            PerfLog.Write($"[차트] {sat.Id} / {SelectedPeriod.Label} / {points.Count}점 / 조회 {queryMs} ms / 차트 채우기 {fillMs} ms");
        }
        catch (Exception ex)
        {
            ChartInfo = $"조회 실패: {ex.Message}";
        }
        finally
        {
            _loading = false;
            if (_reloadPending)
            {
                _reloadPending = false;
                _ = ReloadChartAsync();
            }
        }
    }

    private void ClearChart()
    {
        _avgSeries.Points.Clear();
        _bandSeries.Points.Clear();
        _bandSeries.Points2.Clear();
        BatteryPlot.InvalidatePlot(true);
    }

    // ═════════ ⑦ 알람 ═════════

    private void OnAlarmRaised(object? sender, AlarmEvent alarm) => AddAlarm(alarm);

    private void AddAlarm(AlarmEvent alarm)
    {
        Alarms.Insert(0, alarm);                 // 최신 알람을 맨 위에
        if (Alarms.Count > MaxAlarms)
            Alarms.RemoveAt(Alarms.Count - 1);   // 오래된 것 정리
    }
}
