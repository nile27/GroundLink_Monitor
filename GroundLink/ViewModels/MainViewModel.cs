using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GroundLink.Data;
using GroundLink.Models;
using GroundLink.Services;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
// socket client 연결 부
using System.Net.Sockets;

namespace GroundLink.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public const string PeriodDay = "day";
    public const string PeriodWeek = "week";

    private readonly TelemetryRepository repository;
    private readonly string satelliteId;
    private readonly TelemetryReceiver receiver = new TelemetryReceiver();   // 수신 스레드는 여기서 관리

    private readonly Action<Action> runOnUi;   // UI 스레드에서 실행해 주는 함수 (창이 넣어 줌)


    public event Action<HistoryChartData>? HistoryLoaded;
    public MainViewModel(TelemetryRepository repository, string satelliteId, Action<Action> runOnUi)
    {
        this.repository = repository;
        this.satelliteId = satelliteId;
        this.runOnUi = runOnUi;

        // 수신 이벤트는 수신 스레드에서 옴 → 화면 값은 UI 스레드에서 바꿈
        receiver.Received += data => runOnUi(() => ShowReceived(data));
        receiver.Disconnected += reason => runOnUi(() => Disconnected(reason));
    }



    // 1. 연결 상태
    [ObservableProperty] private string statusText = "연결 안 됨";
    [ObservableProperty] private string statusColor = Theme.Muted;   
    [ObservableProperty] private string statusDetail = "";

    // 2. 현재 값
    [ObservableProperty] private string batteryText = "--";
    [ObservableProperty] private string batteryMarginText = "";
    [ObservableProperty] private string temperatureText = "--";

    // 3. 오류 글자
    [ObservableProperty] private string errorText = "";

    // 4. 과거 기록 기간 버튼 강조
    [ObservableProperty] private bool isDaySelected = true;
    [ObservableProperty] private bool isWeekSelected;

    // 최신 값 조회 명령
    [RelayCommand]
    private void LoadLatest()
    {
        ErrorText = "";

        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Telemetry? latest = repository.GetLatest(satelliteId);
            stopwatch.Stop();
            Debug.WriteLine($"GetLatest {stopwatch.Elapsed.TotalMilliseconds:F1} ms");   // 측정 기록 (출력 창)

            if (latest == null)
            {
                ErrorText = "데이터 없음";
                return;
            }

            BatteryText = $"{latest.Battery:F1} %";
            TemperatureText = $"{latest.Temperature:F1} °C";
            BatteryMarginText = $"{latest.RecordedAt:HH:mm:ss} 기준";
        }
        catch (SqlException ex)
        {
            ErrorText = $"DB 조회 실패: {ex.Message}";
        }
    }

    // 시뮬레이터 연결 명령
    [RelayCommand]
    private void Connect()
    {
        if (receiver.IsConnected)   // 이미 연결 중이면 무시 (버튼 여러 번 눌러도 스레드 1개)
            return;

        ErrorText = "";

        try
        {
            receiver.Start("127.0.0.1", 5000);   // 접속 + 수신 스레드 시작
            StatusText = "수신 중";
            StatusColor = Theme.Ok;
            StatusDetail = "";
        }
        catch (SocketException ex)
        {
            StatusText = "연결 실패";
            StatusColor = Theme.Danger;
            ErrorText = $"시뮬레이터 연결 실패: {ex.Message}";
        }
    }

    // 받은 값 화면 표시 함수 (UI 스레드)
    private void ShowReceived(Telemetry data)
    {
        BatteryText = $"{data.Battery:F1} %";
        TemperatureText = $"{data.Temperature:F1} °C";
        BatteryMarginText = $"{data.RecordedAt:HH:mm:ss} 수신";
    }

    // 연결 끊김 처리 함수
    private void Disconnected(string reason)
    {
        StatusText = "끊김";
        StatusColor = Theme.Danger;
        StatusDetail = reason;
    }



    // 과거 기록 기간 선택 명령
    [RelayCommand]
    private void SelectPeriod(string period)
    {
        IsDaySelected = (period == PeriodDay);
        IsWeekSelected = (period == PeriodWeek);

        if (IsWeekSelected)
            LoadHistory(DateTime.Today.AddDays(-7), DateTime.Today, 86400);   // 지난 7일: 하루 단위 (7칸)
        else
            LoadHistory(DateTime.Today.AddDays(-1), DateTime.Today, 3600);    // 어제: 1시간 단위 (24칸)
    }

    // 과거 기록 요약 조회 함수 → 끝나면 HistoryLoaded로 화면에 알림
    private void LoadHistory(DateTime from, DateTime to, int bucketSeconds)
    {
        ErrorText = "";

        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            List<SummaryPoint> points = repository.GetSummary(satelliteId, from, to, bucketSeconds);
            stopwatch.Stop();
            Debug.WriteLine($"GetSummary {points.Count}줄 {stopwatch.Elapsed.TotalMilliseconds:F1} ms");

            if (points.Count == 0)
                ErrorText = "과거 기록 데이터 없음";

            HistoryLoaded?.Invoke(new HistoryChartData(points, bucketSeconds));
        }
        catch (SqlException ex)
        {
            ErrorText = $"DB 조회 실패: {ex.Message}";
        }
    }
}

// 과거 기록 차트에 넘길 값 
public record HistoryChartData(IReadOnlyList<SummaryPoint> Points, int BucketSeconds);
