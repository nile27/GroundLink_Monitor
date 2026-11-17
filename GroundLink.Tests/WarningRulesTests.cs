using GroundLink.Rules;

namespace GroundLink.Tests;

/*
 * 경고 판정 테스트: 기준값 바로 위·아래에서 결과가 맞는지 (경계값).
 * 배터리 60 % "미만"이 경고 → 60은 정상, 59.9는 경고
 * 온도 0 °C "미만" 또는 40 °C "초과"가 경고 → 0과 40은 정상
 */
public class WarningRulesTests
{
    [Theory]
    [InlineData(60.0, false)]
    [InlineData(59.9, true)]
    [InlineData(100.0, false)]
    [InlineData(0.0, true)]
    public void 배터리_경고는_60퍼센트_미만(double battery, bool expected)
    {
        Assert.Equal(expected, WarningRules.IsBatteryWarning(battery));
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(40.0, false)]
    [InlineData(-0.1, true)]
    [InlineData(40.1, true)]
    [InlineData(15.0, false)]
    public void 온도_경고는_0도_미만_또는_40도_초과(double temperature, bool expected)
    {
        Assert.Equal(expected, WarningRules.IsTemperatureWarning(temperature));
    }

    [Fact]
    public void 배터리_여유는_경고선까지_남은_퍼센트포인트()
    {
        Assert.Equal(36.1, WarningRules.BatteryMargin(96.1), precision: 6);
        Assert.True(WarningRules.BatteryMargin(55) < 0);   // 경고 중이면 음수
    }

    [Theory]
    [InlineData(15.0, 15.0)]   // 0까지 15, 40까지 25 → 가까운 쪽 15
    [InlineData(35.0, 5.0)]    // 0까지 35, 40까지 5 → 5
    public void 온도_여유는_가까운_경고선까지(double temperature, double expected)
    {
        Assert.Equal(expected, WarningRules.TemperatureMargin(temperature), precision: 6);
    }
}
