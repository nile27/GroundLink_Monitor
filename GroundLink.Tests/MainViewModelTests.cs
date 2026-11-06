using GroundLink.Models;
using GroundLink.ViewModels;
using Xunit;

namespace GroundLink.Tests;

/// <summary>
/// MainViewModel, SatelliteStatus 단위 테스트.
/// 모든 테스트는 "준비 → 실행 → 확인" 순서.
/// </summary>
public class MainViewModelTests
{
    // 테스트마다 같은 준비를 반복하지 않으려고 만든 도우미
    private static (StubSatelliteService Stub, MainViewModel Vm) CreateVm()
    {
        var stub = new StubSatelliteService();
        var vm = new MainViewModel(stub, new StubTelemetryRepository());
        return (stub, vm);
    }

    [Fact]
    public void 배터리가_20_미만이면_경고상태다()
    {
        var sat = new SatelliteStatus { Id = "SAT-X", Battery = 15 };

        Assert.True(sat.IsWarning);
    }

    [Fact]
    public void SAFE_모드면_배터리가_높아도_경고상태다()
    {
        var sat = new SatelliteStatus { Id = "SAT-X", Battery = 90, Mode = SatelliteMode.Safe };

        Assert.True(sat.IsWarning);
    }

    [Fact]
    public async Task 모드전환_명령은_NORMAL을_SAFE로_바꾼다()
    {
        var (stub, vm) = CreateVm();
        var target = vm.Satellites[0];

        await vm.ToggleSafeModeCommand.ExecuteAsync(target);

        Assert.Equal("SAT-0001", stub.LastCommandId);
        Assert.Equal(SatelliteMode.Safe, stub.LastCommandMode);
        Assert.Equal(SatelliteMode.Safe, target.Mode);
    }

    [Fact]
    public void 선택된_위성이_없으면_모드전환_버튼은_비활성이다()
    {
        var (_, vm) = CreateVm();

        Assert.False(vm.ToggleSafeModeCommand.CanExecute(null));
    }

    [Fact]
    public void 알람은_최신이_맨_위에_쌓인다()
    {
        var (stub, vm) = CreateVm();

        stub.FireAlarm("SAT-0001", "첫 번째");
        stub.FireAlarm("SAT-0002", "두 번째");

        Assert.Equal(2, vm.Alarms.Count);
        Assert.Equal("두 번째", vm.Alarms[0].Message);
    }

    [Fact]
    public void 알람은_100개를_넘지_않는다()
    {
        var (stub, vm) = CreateVm();

        for (int i = 0; i < 150; i++)
            stub.FireAlarm("SAT-0001", $"알람 {i}");

        Assert.Equal(100, vm.Alarms.Count);
    }

    [Fact]
    public void 요약은_경고_위성_수를_센다()
    {
        var (stub, vm) = CreateVm();
        stub.Items[1].Battery = 10; // 경고 상태

        stub.FireUpdated();

        Assert.Equal(2, vm.TotalCount);
        Assert.Equal(1, vm.WarningCount);
    }

    // ───────── 낙관적 업데이트 ─────────

    [Fact]
    public async Task 모드전환은_명령이_끝나기_전에_화면에_먼저_반영된다()
    {
        var (stub, vm) = CreateVm();
        var target = vm.Satellites[0];
        stub.HoldCommands = new TaskCompletionSource(); // 서버 응답이 아직 안 온 상태

        var running = vm.ToggleSafeModeCommand.ExecuteAsync(target);

        Assert.False(running.IsCompleted);              // 명령은 아직 진행 중인데
        Assert.Equal(SatelliteMode.Safe, target.Mode);  // 화면은 이미 SAFE
        Assert.Equal(1, vm.SafeCount);                  // 요약 바도 바로 반영

        stub.HoldCommands.SetResult();
        await running;
        Assert.Equal(SatelliteMode.Safe, target.Mode);
    }

    [Fact]
    public async Task 모드전환_명령이_실패하면_원래_모드로_되돌리고_알람에_기록한다()
    {
        var (stub, vm) = CreateVm();
        var target = vm.Satellites[0];
        stub.FailCommands = true;

        await vm.ToggleSafeModeCommand.ExecuteAsync(target);

        Assert.Equal(SatelliteMode.Normal, target.Mode);
        Assert.Equal(0, vm.SafeCount);
        Assert.Contains("명령 실패", vm.Alarms[0].Message);
    }

    // ───────── SAFE 전체 복귀 ─────────

    [Fact]
    public async Task SAFE_전체_복귀는_SAFE인_위성에만_NORMAL_명령을_보낸다()
    {
        var (stub, vm) = CreateVm();
        stub.Items.Add(new SatelliteStatus { Id = "SAT-0003", Battery = 70, Mode = SatelliteMode.Safe });
        stub.Items[0].Mode = SatelliteMode.Safe;
        stub.FireUpdated(); // 새 위성 추가 + 요약 갱신 → 버튼 활성

        await vm.RestoreAllSafeCommand.ExecuteAsync(null);

        Assert.Equal(2, stub.Commands.Count);
        Assert.All(stub.Commands, c => Assert.Equal(SatelliteMode.Normal, c.Mode));
        Assert.Equal(new[] { "SAT-0001", "SAT-0003" }, stub.Commands.Select(c => c.Id).OrderBy(id => id));
        Assert.Equal(0, vm.SafeCount);
    }

    [Fact]
    public void SAFE_위성이_없으면_전체_복귀_버튼은_비활성이다()
    {
        var (_, vm) = CreateVm();

        Assert.False(vm.RestoreAllSafeCommand.CanExecute(null));
    }
}
