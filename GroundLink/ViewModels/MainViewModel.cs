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
    public const string PeriodMonth = "month";

    private readonly TelemetryRepository repository;
    private readonly string satelliteId;
    private readonly TelemetryReceiver receiver = new TelemetryReceiver();   // 수신 서비스
    private readonly TelemetrySaver saver; // 저장 서비스
    private readonly string simulatorHost = Environment.GetEnvironmentVariable("GROUNDLINK_SIM_HOST") ?? "127.0.0.1";
  

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);   // 연결 시간 제한
    private PeriodicTimer? dailyTimer;                    // 날짜 확인 타이머 - 1분
    private Task dailyReady = Task.CompletedTask;         // 시작 시 요약 채우기 작업


    // 과거 차트용 이벤트 (DB)
    public event Action<HistoryChartData>? HistoryLoaded;

    // 실시간 차트용 이벤트
    public event Action<Telemetry>? TelemetryReceived;


    public MainViewModel(TelemetryRepository repository, string satelliteId, Action<Action> runOnUi)
    {
        this.repository = repository;
        this.satelliteId = satelliteId;
        saver = new TelemetrySaver(repository);

        // 수신 이벤트 연결
        receiver.Received += data => runOnUi(() => ShowReceived(data));
        receiver.Received += data => saver.Add(data);
        receiver.Disconnected += reason => saver.Flush();
        receiver.Disconnected += reason => runOnUi(() => Disconnected(reason));

        // 저장 이벤트 연결
        saver.Saved += count => runOnUi(() => StatusDetail = $"DB 저장 {count}건");
        saver.SaveFailed += message => runOnUi(() => ErrorText = message);

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
    [ObservableProperty] private bool isMonthSelected;

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
            Debug.WriteLine($"GetLatest {stopwatch.Elapsed.TotalMilliseconds:F1} ms");   // 측정 기록

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
    private async Task Connect()
    {
        if (receiver.IsConnected)   // 이미 연결 중
            return;

        ErrorText = "";
        StatusText = "연결 중";
        StatusColor = Theme.Caution;
        StatusDetail = "";

        try
        {
            await receiver.StartAsync(simulatorHost, 5000, ConnectTimeout);
            StatusText = "수신 중";
            StatusColor = Theme.Ok;
        }
        catch (OperationCanceledException)   // 시간 안에 응답 없음
        {
            StatusText = "연결 실패";
            StatusColor = Theme.Danger;
            StatusDetail = "시뮬레이터 응답 없음";
        }
        catch (SocketException ex)
        {
            StatusText = "연결 실패";
            StatusColor = Theme.Danger;
            StatusDetail = ex.SocketErrorCode == SocketError.ConnectionRefused
                ? "시뮬레이터가 실행 중인지 확인"
                : $"{ex.SocketErrorCode}";

        }
    }

    // 받은 값 화면 표시 함수
    private void ShowReceived(Telemetry data)
    {
        BatteryText = $"{data.Battery:F1} %";
        TemperatureText = $"{data.Temperature:F1} °C";
        BatteryMarginText = $"{data.RecordedAt:HH:mm:ss} 수신";

        TelemetryReceived?.Invoke(data);
    }

    // 연결 끊김 처리 함수
    private void Disconnected(string reason)
    {
        StatusText = "끊김";
        StatusColor = Theme.Danger;
        StatusDetail = reason;
    }

    public void Shutdown()
    {
        dailyTimer?.Dispose();   // 날짜 확인 타이머 멈춤

        if (receiver.IsConnected)
            receiver.Stop(TimeSpan.FromSeconds(2));
    }



    // 과거 기록 기간 선택 명령
    [RelayCommand]
    private async Task SelectPeriod(string period)
    {
        IsDaySelected = (period == PeriodDay);
        IsWeekSelected = (period == PeriodWeek);
        IsMonthSelected = (period == PeriodMonth);

        if (IsMonthSelected)
            await LoadDailyHistoryAsync(30);    // 지난 30일, 하루 요약
        else if (IsWeekSelected)
            await LoadDailyHistoryAsync(7);     // 지난 7일, 하루 요약
        else
            await LoadHistoryAsync(DateTime.Today.AddDays(-1), DateTime.Today, 3600);    // 어제, 1시간 단위
    }

    // 과거 기록 요약 조회 함수
    private async Task LoadHistoryAsync(DateTime from, DateTime to, int bucketSeconds)
    {
        ErrorText = "";

        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            var (battery, temperature) = await repository.GetSummariesAsync(satelliteId, from, to, bucketSeconds);
            stopwatch.Stop();
            Debug.WriteLine($"GetSummary {battery.Count}줄 {stopwatch.Elapsed.TotalMilliseconds:F1} ms");

            if (battery.Count == 0)
                ErrorText = "과거 기록 데이터 없음";

            HistoryLoaded?.Invoke(new HistoryChartData(battery, temperature, bucketSeconds, from, to));
        }
        catch (SqlException ex)
        {
            ErrorText = $"DB 조회 실패: {ex.Message}";
        }
    }

    // 하루 요약 자동 저장 시작 함수
    public async Task StartDailySummaryAsync()
    {
        dailyReady = FillDailySummaryAsync();
        await dailyReady;

        DateTime lastDay = DateTime.Today;
        dailyTimer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await dailyTimer.WaitForNextTickAsync())
        {
            if (DateTime.Today != lastDay)   // 날짜 바뀜
            {
                lastDay = DateTime.Today;
                await FillDailySummaryAsync();
            }
        }
    }

    // 지난 30일 하루 요약 채우기 함수
    private async Task FillDailySummaryAsync()
    {
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            await repository.FillDailyAsync(satelliteId, DateTime.Today.AddDays(-30), DateTime.Today);
            stopwatch.Stop();
            Debug.WriteLine($"FillDaily {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
        }
        catch (SqlException ex)
        {
            ErrorText = $"하루 요약 저장 실패: {ex.Message}";
        }
    }

    // 하루 요약 조회 함수
    private async Task LoadDailyHistoryAsync(int days)
    {
        ErrorText = "";
        DateTime from = DateTime.Today.AddDays(-days);
        DateTime to = DateTime.Today;

        try
        {
            await dailyReady;

            Stopwatch stopwatch = Stopwatch.StartNew();
            var (battery, temperature) = await repository.GetDailyAsync(satelliteId, from, to);
            stopwatch.Stop();
            Debug.WriteLine($"GetDaily {battery.Count}줄 {stopwatch.Elapsed.TotalMilliseconds:F1} ms");

            if (battery.Count == 0)
                ErrorText = "과거 기록 데이터 없음";

            HistoryLoaded?.Invoke(new HistoryChartData(battery, temperature, 86400, from, to));
        }
        catch (SqlException ex)
        {
            ErrorText = $"DB 조회 실패: {ex.Message}";
        }
    }
       
}

// 과거 기록 차트에 넘길 값 
public record HistoryChartData(IReadOnlyList<SummaryPoint> Battery, IReadOnlyList<SummaryPoint> Temperature, int BucketSeconds,
                               DateTime From, DateTime To);
