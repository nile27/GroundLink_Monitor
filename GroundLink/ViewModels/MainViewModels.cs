using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GroundLink.Models;
using GroundLink.Services;

namespace GroundLink.ViewModels;

/// <summary>
/// 메인 화면용 ViewModel.
/// 서비스에서 위성 목록을 받아 화면이 바인딩할 수 있게 들고 있는다.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ISatelliteService _service;

    // 화면 목록에 바인딩할 컬렉션
    // ObservableCollection: 항목이 추가·삭제되면 화면에 알림 (List는 알림 없음)
    public ObservableCollection<SatelliteStatus> Satellites { get; }

    // 생성자 주입: DI 컨테이너가 ISatelliteService 구현체를 넣어줌
    // ViewModel은 그게 Fake인지 Api인지 모름
    public MainViewModel(ISatelliteService service)
    {
        _service = service;
        Satellites = new ObservableCollection<SatelliteStatus>(_service.Satellites);
        _service.Start();
    }

    // [RelayCommand]: 이 메서드를 버튼에 연결할 수 있는 ToggleSafeModeCommand 속성을 자동 생성
    // (메서드 이름 끝의 Async는 빠지고 Command가 붙음)
    [RelayCommand]
    private async Task ToggleSafeModeAsync(SatelliteStatus satellite)
    {
        // 지금 SAFE면 NORMAL로, 아니면 SAFE로
        var next = satellite.Mode == "SAFE" ? "NORMAL" : "SAFE";
        await _service.SetModeAsync(satellite.Id, next);
    }
}