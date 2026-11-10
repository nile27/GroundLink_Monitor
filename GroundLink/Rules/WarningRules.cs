namespace GroundLink.Rules;

/*
 * 경고 판정 (DOCS/requirements.md 데이터 요구사항).
 * 순수 함수: 입력만으로 결과가 정해지고 외부 상태를 바꾸지 않음.
 */
public static class WarningRules
{
    public const double BatteryWarning = 60;    // % 미만 (LEO DoD 상한 40% 초과)
    public const double TemperatureLow = 0;     // °C 미만 (리튬이온 충전 하한)
    public const double TemperatureHigh = 40;   // °C 초과 (LEO 배터리 온도 범위 상한)

    /// <summary>배터리 잔량이 경고 기준 미만인지.</summary>
    public static bool IsBatteryWarning(double battery) =>
        battery < BatteryWarning;

    /// <summary>배터리 온도가 정상 범위(0~40 °C)를 벗어났는지.</summary>
    public static bool IsTemperatureWarning(double temperature) =>
        temperature < TemperatureLow || temperature > TemperatureHigh;

    /// <summary>배터리가 경고 기준까지 남은 여유 (%p). 음수면 경고 중.</summary>
    public static double BatteryMargin(double battery) =>
        battery - BatteryWarning;

    /// <summary>배터리 온도가 가까운 쪽 경고 기준까지 남은 여유 (°C). 음수면 경고 중.</summary>
    public static double TemperatureMargin(double temperature) =>
        Math.Min(temperature - TemperatureLow, TemperatureHigh - temperature);
}
