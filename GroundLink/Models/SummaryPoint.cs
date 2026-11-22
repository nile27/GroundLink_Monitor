namespace GroundLink.Models;

// 구간 요약 1개 (구간 시작 시각, 평균, 최소, 최대)
public record SummaryPoint(DateTime BucketStart, double Avg, double Min, double Max);