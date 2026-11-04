using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GroundLink.Models;
using GroundLink.Services;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace GroundLink.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // ─────────────────────────────────────────
    // ① 필드: 서비스, 그래프 선, 알람 최대 개수
    // ─────────────────────────────────────────
    private readonly ISatelliteService _service;
    private readonly LineSeries _batterySeries = new() { Title = "배터리" };
    private const int MaxAlarms = 100;

    // ─────────────────────────────────────────
    // ② 화면이 바인딩하는 속성
    // ─────────────────────────────────────────
    public ObservableCollection<SatelliteStatus> Satellites { get; }   // 카드 목록
    public ObservableCollection<AlarmEvent> Alarms { get; } = new();   // 알람 표
    public PlotModel BatteryPlot { get; }                              // 그래프

    [ObservableProperty]
    private SatelliteStatus? selectedSatellite;                        // 선택된 위성

    // ─────────────────────────────────────────
    // ③ 생성자: 준비 + 이벤트 구독 + 시작
    // ─────────────────────────────────────────
    public MainViewModel(ISatelliteService service)
    {
        _service = service;

        Satellites = new ObservableCollection<SatelliteStatus>(_service.Satellites);
        BatteryPlot = CreateBatteryPlot();

        _service.Updated += OnServiceUpdated;
        _service.AlarmRaised += OnAlarmRaised;

        _service.Start();
    }

    // ─────────────────────────────────────────
    // ④ 버튼 명령: 모드 전환
    // ─────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanToggleSafeMode))]
    private async Task ToggleSafeModeAsync(SatelliteStatus? satellite)
    {
        if (satellite is null) return;

        var next = satellite.Mode == "SAFE" ? "NORMAL" : "SAFE";
        await _service.SetModeAsync(satellite.Id, next);
    }

    private bool CanToggleSafeMode(SatelliteStatus? satellite) => satellite is not null;

    // ─────────────────────────────────────────
    // ⑤ 그래프
    // ─────────────────────────────────────────
    private PlotModel CreateBatteryPlot()
    {
        var model = new PlotModel { Title = "배터리 (%)" };
        model.Axes.Add(new DateTimeAxis { Position = AxisPosition.Bottom, StringFormat = "HH:mm:ss" });
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Minimum = 0, Maximum = 100 });
        model.Series.Add(_batterySeries);
        return model;
    }

    // 선택이 바뀌면 자동 호출
    partial void OnSelectedSatelliteChanged(SatelliteStatus? value) => ReloadChart();

    // 서비스가 1초 갱신을 끝내면 호출
    private void OnServiceUpdated(object? sender, EventArgs e) => ReloadChart();

    private void ReloadChart()
    {
        _batterySeries.Points.Clear();

        if (SelectedSatellite is not null)
        {
            foreach (var p in _service.GetHistory(SelectedSatellite.Id))
                _batterySeries.Points.Add(DateTimeAxis.CreateDataPoint(p.Time, p.Battery));
        }

        BatteryPlot.InvalidatePlot(true);
    }

    // ─────────────────────────────────────────
    // ⑥ 알람
    // ─────────────────────────────────────────
    private void OnAlarmRaised(object? sender, AlarmEvent alarm)
    {
        Alarms.Insert(0, alarm);
        if (Alarms.Count > MaxAlarms)
            Alarms.RemoveAt(Alarms.Count - 1);
    }
}