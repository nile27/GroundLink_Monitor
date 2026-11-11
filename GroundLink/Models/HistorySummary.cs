namespace GroundLink.Models;

/*
 * 기간 요약 결과 (과거 기록 차트용). 같은 구간으로 묶은 배터리·배터리 온도 요약.
 *
 * Battery      : 구간별 배터리 잔량 평균·최소·최대 (%)
 * Temperature  : 구간별 배터리 온도 평균·최소·최대 (°C)
 */
public record HistorySummary(List<SummaryPoint> Battery, List<SummaryPoint> Temperature);
