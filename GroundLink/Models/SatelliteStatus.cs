using CommunityToolkit.Mvvm.ComponentModel;

namespace GroundLink.Models;
public partial class SatelliteStatus : ObservableObject
{
    // 위성 ID (예: "SAT-001")
    // 한 번 정해지면 바뀌지 않으므로 알림 기능이 필요 없는 일반 속성
    // init: 객체를 만들 때만 값을 넣을 수 있고 이후엔 변경 불가
    public string Id { get; init; } = "";

    // 배터리 잔량 (%)
    // [ObservableProperty]가 이 필드를 보고 빌드 시 Battery 속성을 자동 생성함
    // 화면 바인딩은 대문자 Battery를 사용
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWarning))] // Battery가 바뀌면 IsWarning도 바뀌었다고 같이 알림
    private double battery;

    // 위성 내부 온도 (°C) → Temperature 속성 자동 생성
    [ObservableProperty]
    private double temperature;

    // 운용 모드: "NORMAL"(정상) 또는 "SAFE"(안전 모드) → Mode 속성 자동 생성
    // 기본값은 NORMAL
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWarning))]
    private string mode = "NORMAL";

    // 지상국과 교신 중인지 여부 → InContact 속성 자동 생성
    [ObservableProperty]
    private bool inContact;

    // 계산 속성: 저장하는 값이 아니라 Battery와 Mode로 그때그때 계산
    // 경고 상태 판단 규칙은 여기 한 곳에만 있음
    public bool IsWarning => Mode == "SAFE" || Battery < 20;
}