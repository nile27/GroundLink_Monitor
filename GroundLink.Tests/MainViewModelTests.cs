using System.ComponentModel;
using GroundLink.Data;
using GroundLink.Models;
using GroundLink.ViewModels;

namespace GroundLink.Tests;

/*
 * ViewModel 테스트: 화면(WPF) 없이 값만 확인.
 * runOnUi에 "바로 실행"을 넘겨서 Dispatcher 없이 동작시킴.
 * DB·시뮬레이터에 접속하는 기능(최신 값 조회, 연결, 과거 기록)은 여기서 다루지 않음 → 앱에서 수동 테스트
 */
public class MainViewModelTests
{
    /// <summary>테스트용 ViewModel. 연결 문자열은 쓰지 않는 값 (아래 테스트는 DB에 접속하지 않음).</summary>
    private static MainViewModel CreateViewModel() =>
        new(new TelemetryRepository("unused"), "SAT-001", "127.0.0.1", 5000, action => action());

    private static Telemetry Sample(double battery, double temperature) =>
        new("SAT-001", new DateTime(2026, 11, 12, 10, 0, 0), battery, temperature);

    [Fact]
    public void 정상_값이면_숫자와_경고까지_여유를_보통_색으로()
    {
        var vm = CreateViewModel();

        vm.ShowCurrentValues(Sample(96.1, 14.3));

        Assert.Equal("96.1 %", vm.BatteryText);
        Assert.Equal("경고까지 36.1 %p", vm.BatteryMarginText);
        Assert.Equal(Theme.Text, vm.BatteryColor);
        Assert.Equal("14.3 °C", vm.TemperatureText);
        Assert.Equal("경고까지 14.3 °C", vm.TemperatureMarginText);
    }

    [Fact]
    public void 배터리_경고면_경고_문구와_빨간색()
    {
        var vm = CreateViewModel();

        vm.ShowCurrentValues(Sample(55.0, 14.3));

        Assert.Equal("경고: 60 % 미만", vm.BatteryMarginText);
        Assert.Equal(Theme.Danger, vm.BatteryColor);
        Assert.Equal(Theme.Danger, vm.BatteryMarginColor);
        Assert.Equal(Theme.Text, vm.TemperatureColor);   // 온도는 정상
    }

    [Fact]
    public void 기간을_바꾸면_버튼_강조용_값도_바뀌었다고_알림()
    {
        var vm = CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SelectedPeriod = MainViewModel.PeriodDay;

        Assert.True(vm.IsDaySelected);
        Assert.False(vm.IsWeekSelected);
        Assert.Contains(nameof(MainViewModel.IsDaySelected), changed);   // [NotifyPropertyChangedFor] 동작
        Assert.Contains(nameof(MainViewModel.IsWeekSelected), changed);
    }

    [Fact]
    public void 같은_값을_다시_넣으면_알리지_않음()
    {
        var vm = CreateViewModel();
        vm.ShowCurrentValues(Sample(96.1, 14.3));
        int count = 0;
        vm.PropertyChanged += (_, _) => count++;

        vm.ShowCurrentValues(Sample(96.1, 14.3));

        Assert.Equal(0, count);   // 화면 갱신이 불필요하게 일어나지 않음
    }

    [Fact]
    public void 연결_전에는_연결_버튼을_누를_수_있음()
    {
        var vm = CreateViewModel();

        Assert.True(vm.ConnectCommand.CanExecute(null));
        Assert.Equal("연결 안 됨", vm.ConnectionText);
    }
}
