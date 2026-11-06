using CommunityToolkit.Mvvm.ComponentModel;

namespace GroundLink.Models;

/// <summary>
/// 위성 운용 모드 이름. 문자열을 여기저기 직접 쓰지 않으려고 한 곳에 모아둠.
/// </summary>
public static class SatelliteMode
{
    public const string Normal = "NORMAL"; // 정상 운용
    public const string Safe = "SAFE";     // 비상 절전 모드
}

/// <summary>
/// 위성 한 대의 "현재" 상태.
/// ObservableObject를 상속해서, 값이 바뀌면 화면에 자동으로 알린다.
/// </summary>
public partial class SatelliteStatus : ObservableObject
{
    /// <summary>이 값 미만이면 배터리 부족으로 본다 (%)</summary>
    public const double LowBatteryThreshold = 20;

    /// <summary>위성 ID (예: SAT-0001). 한 번 정해지면 안 바뀜</summary>
    public string Id { get; init; } = "";

    /// <summary>배터리 (%). 바뀌면 IsWarning도 다시 계산하라고 같이 알림</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWarning))]
    private double battery;

    /// <summary>내부 온도 (°C)</summary>
    [ObservableProperty]
    private double temperature;

    /// <summary>운용 모드 (NORMAL / SAFE). 바뀌면 IsWarning도 같이 알림</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWarning))]
    private string mode = SatelliteMode.Normal;

    /// <summary>지상국과 교신 중인지</summary>
    [ObservableProperty]
    private bool inContact;

    /// <summary>
    /// 경고 상태 (화면에서 카드를 빨갛게 칠할 때 사용).
    /// 저장하는 값이 아니라 Battery, Mode로 그때그때 계산한다.
    /// </summary>
    public bool IsWarning => Mode == SatelliteMode.Safe || Battery < LowBatteryThreshold;
}
