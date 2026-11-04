using GroundLink.Models;
using GroundLink.ViewModels;
using Xunit;

namespace GroundLink.Tests;

public class MainViewModelTests
{
    // [Fact] = 테스트 하나. 모든 테스트는 "준비 → 실행 → 확인" 순서

    [Fact]
    public void 배터리가_20_미만이면_경고상태다()
    {
        var sat = new SatelliteStatus { Id = "SAT-X", Battery = 15 };   // 준비

        Assert.True(sat.IsWarning);                                    // 확인
    }

    [Fact]
    public void SAFE_모드면_배터리가_높아도_경고상태다()
    {
        var sat = new SatelliteStatus { Id = "SAT-X", Battery = 90, Mode = "SAFE" };

        Assert.True(sat.IsWarning);
    }

    [Fact]
    public async Task 모드전환_명령은_NORMAL을_SAFE로_바꾼다()
    {
        // 준비
        var stub = new StubSatelliteService();
        var vm = new MainViewModel(stub);
        var target = vm.Satellites[0];

        // 실행: 버튼 누른 것과 같은 동작
        await vm.ToggleSafeModeCommand.ExecuteAsync(target);

        // 확인
        Assert.Equal("SAT-001", stub.LastCommandId);
        Assert.Equal("SAFE", stub.LastCommandMode);
        Assert.Equal("SAFE", target.Mode);
    }

    [Fact]
    public void 선택된_위성이_없으면_모드전환_버튼은_비활성이다()
    {
        var vm = new MainViewModel(new StubSatelliteService());

        Assert.False(vm.ToggleSafeModeCommand.CanExecute(null));
    }

    [Fact]
    public void 알람은_최신이_맨_위에_쌓인다()
    {
        var stub = new StubSatelliteService();
        var vm = new MainViewModel(stub);

        stub.FireAlarm("SAT-001", "첫 번째");
        stub.FireAlarm("SAT-002", "두 번째");

        Assert.Equal(2, vm.Alarms.Count);
        Assert.Equal("두 번째", vm.Alarms[0].Message);
    }

    [Fact]
    public void 알람은_100개를_넘지_않는다()
    {
        var stub = new StubSatelliteService();
        var vm = new MainViewModel(stub);

        for (int i = 0; i < 150; i++)
            stub.FireAlarm("SAT-001", $"알람 {i}");

        Assert.Equal(100, vm.Alarms.Count);
    }
}