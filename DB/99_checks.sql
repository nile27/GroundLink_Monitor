-- ═══════════════════════════════════════════
-- 99. 확인용 조회 모음
-- 필요한 쿼리만 드래그해서 F5
-- ═══════════════════════════════════════════

USE MonitorDB;
GO

-- 전체 줄 수, 시작·끝 시각 (604,800 / 7일 전 / 방금 전이면 OK)
SELECT COUNT(*)        AS 줄수,
       MIN(RecordedAt) AS 시작,
       MAX(RecordedAt) AS 끝
FROM dbo.Telemetry;

-- 최신 10줄
SELECT TOP 10 *
FROM dbo.Telemetry
ORDER BY RecordedAt DESC;

-- 최신 1줄 (WPF 2단계에서 쓸 쿼리)
SELECT TOP 1 RecordedAt, Battery, Temperature
FROM dbo.Telemetry
WHERE SatelliteId = 'SAT-001'
ORDER BY RecordedAt DESC;

-- 최근 1시간 줄 수 (3,600 근처면 OK)
SELECT COUNT(*) AS 최근1시간
FROM dbo.Telemetry
WHERE RecordedAt >= DATEADD(HOUR, -1, SYSDATETIME());

-- 1주일을 30분 단위로 요약 (WPF 3단계 차트에서 쓸 방식, 약 336줄)
SELECT DATE_BUCKET(MINUTE, 30, RecordedAt) AS 구간,
       MIN(Battery) AS 최소,
       MAX(Battery) AS 최대,
       AVG(Battery) AS 평균
FROM dbo.Telemetry
WHERE SatelliteId = 'SAT-001'
GROUP BY DATE_BUCKET(MINUTE, 30, RecordedAt)
ORDER BY 구간;
