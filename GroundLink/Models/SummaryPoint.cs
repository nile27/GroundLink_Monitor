namespace GroundLink.Models;

/*
 * 기간 요약 1구간 (과거 기록 차트용). 배터리·온도 공통.
 *
 * Bucket : 구간 시작 시각
 * Avg    : 구간 평균
 * Min    : 구간 최소
 * Max    : 구간 최대
 */
public record SummaryPoint(DateTime Bucket, double Avg, double Min, double Max);
