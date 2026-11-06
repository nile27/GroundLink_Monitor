-- ═════════════════════════════════════════════
-- 차트 테스트용 더미 데이터: 위성 100대 × 하루(86,400초) = 864만 줄
-- "지금 기준 24시간 전 ~ 지금"으로 만들어짐. 앱은 끄고 실행 (1~3분)
-- ═════════════════════════════════════════════

USE GroundLink;
GO

TRUNCATE TABLE dbo.Telemetry;  -- 기존 데이터 비우기

DECLARE @start DATETIME2(3) = DATEADD(DAY, -1, SYSDATETIME());

INSERT INTO dbo.Telemetry (SatelliteId, RecordedAt, Battery, Temperature, Mode, InContact)
SELECT
    'SAT-' + RIGHT('0000' + CAST(sat.value AS VARCHAR(4)), 4),            -- SAT-0001 ~ SAT-0100
    DATEADD(SECOND, sec.value, @start),                                    -- 1초 간격
    60 + 30 * SIN(2 * PI() * (sec.value + sat.value * 300) / 5400.0),      -- 배터리: 90분 주기 물결
    10 + 15 * SIN(2 * PI() * (sec.value + sat.value * 300) / 5400.0),      -- 온도: 같은 주기
    'NORMAL',
    CASE WHEN (sec.value + sat.value * 300) % 5400 < 600 THEN 1 ELSE 0 END -- 90분 중 10분 교신
FROM GENERATE_SERIES(1, 100) AS sat           -- 위성 번호 1~100
CROSS JOIN GENERATE_SERIES(0, 86399) AS sec;  -- 하루의 초 0~86399
GO
