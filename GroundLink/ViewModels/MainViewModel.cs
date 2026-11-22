using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GroundLink.Data;
using GroundLink.Models;
using Microsoft.Data.SqlClient;

namespace GroundLink.ViewModels;

/*
 * 메인 화면의 값과 동작 (ViewModel).
 * 화면 컨트롤(TextBlock, Button 등)을 모름: 값만 가지고, 값이 바뀌면 알림 → WPF 바인딩이 화면을 갱신.
 * 색은 WPF Brush 대신 "#RRGGBB" 글자로 가짐 → WPF 타입이 없어서 화면 없이 테스트 가능.
 *
 * [ObservableProperty]: 필드 하나로 "속성 + 변경 알림" 코드를 빌드 때 자동 생성 (CommunityToolkit.Mvvm)
 *   private string batteryText → public string BatteryText { get; set; } + PropertyChanged
 *   자동 생성 코드를 이 클래스에 합치려면 클래스가 partial이어야 함
 *
 * [RelayCommand]: 메서드 하나로 버튼에 연결할 Command 속성을 자동 생성
 *   private void LoadLatest() → public IRelayCommand LoadLatestCommand
 *
 * 1. 연결 상태: 글자, 색, 상세 글자
 * 2. 현재 값: 배터리·온도 글자
 * 3. 오류 글자
 * 4. 과거 기록 기간 선택 (버튼 강조)
 * 5. 명령: 최신 값 조회, 시뮬레이터 연결, 기간 선택
 * 6. 과거 기록 조회 (차트는 바인딩이 안 돼서 HistoryLoaded로 화면에 알림)
 */
public partial class MainViewModel : ObservableObject
{
    // 과거 기록 기간 (XAML 버튼의 CommandParameter와 같은 값)
    public const string PeriodDay = "day";
    public const string PeriodWeek = "week";

    private readonly TelemetryRepository repository;
    private readonly string satelliteId;

    // 과거 기록 차트용: 조회 끝나면 발생. 차트(ScottPlot)는 바인딩이 안 돼서 화면이 받아서 직접 그림
    public event Action<HistoryChartData>? HistoryLoaded;

    // 필요한 것을 만드는 쪽(창)에서 받음 → 테스트 때 다른 값으로 바꿔 넣기 쉬움
    public MainViewModel(TelemetryRepository repository, string satelliteId)
    {
        this.repository = repository;
        this.satelliteId = satelliteId;
    }

    // 1. 연결 상태
    [ObservableProperty] private string statusText = "연결 안 됨";
    [ObservableProperty] private string statusColor = Theme.Muted;   // 배지 테두리·점·글자 색
    [ObservableProperty] private string statusDetail = "";

    // 2. 현재 값
    [ObservableProperty] private string batteryText = "--";
    [ObservableProperty] private string batteryMarginText = "";
    [ObservableProperty] private string temperatureText = "--";

    // 3. 오류 글자
    [ObservableProperty] private string errorText = "";

    // 4. 과거 기록 기간 버튼 강조 (true면 스타일이 하늘색)
    [ObservableProperty] private bool isDaySelected = true;
    [ObservableProperty] private bool isWeekSelected;

    // 최신 값 조회 명령 (버튼: LoadLatestCommand)
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

    // 시뮬레이터 연결 명령 (버튼: ConnectCommand) → 카드 09~에서 구현
    [RelayCommand]
    private void Connect()
    {
    }

    // 과거 기록 기간 선택 명령 (버튼: SelectPeriodCommand, CommandParameter = "day" / "week")
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

// 과거 기록 차트에 넘길 값 (요약 칸들 + 칸 크기 초)
public record HistoryChartData(IReadOnlyList<SummaryPoint> Points, int BucketSeconds);
