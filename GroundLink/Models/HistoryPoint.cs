namespace GroundLink.Models;

/// <summary>
/// 차트의 점 하나 = 일정 구간(예: 3분)을 묶어서 요약한 값.
/// </summary>
/// <param name="Time">구간 시작 시각</param>
/// <param name="Min">구간 안의 최솟값 (순간 급락을 놓치지 않으려고)</param>
/// <param name="Max">구간 안의 최댓값</param>
/// <param name="Avg">구간 평균 (차트의 선)</param>
public record HistoryPoint(DateTime Time, double Min, double Max, double Avg);

/// <summary>
/// 차트 기간 선택 항목 (콤보박스에 표시).
/// </summary>
/// <param name="Label">화면에 보이는 글자 (예: 최근 24시간)</param>
/// <param name="Minutes">기간 길이 (분)</param>
public record PeriodOption(string Label, int Minutes);
