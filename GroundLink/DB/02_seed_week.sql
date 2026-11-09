-- ═══════════════════════════════════════════
-- 02. 위성 1개, 1주일치 더미 데이터 (1초 간격, 604,800줄)
-- 기간: 지금부터 7일 전 ~ 지금
-- 다시 실행하면: 기존 데이터를 비우고 "지금" 기준으로 다시 넣음
--               (시간이 지나서 데이터가 과거로 밀렸을 때 다시 실행)
-- ═══════════════════════════════════════════

USE MonitorDB;
GO

TRUNCATE TABLE dbo.Telemetry;   -- 기존 데이터 비우기
GO

DECLARE @now DATETIME2(0) = SYSDATETIME();   -- 끝 = 지금

INSERT INTO dbo.Telemetry (SatelliteId, RecordedAt, Battery, Temperature)
SELECT
    'SAT-001',

    -- 0초째 = 7일 전, 604,799초째 = 지금 직전
    DATEADD(SECOND, s.value - 604800, @now),

    -- 배터리: 90분(5400초) 주기로 30~90% 오르내리는 물결 + ±0.5 흔들림
    60 + 30 * SIN(2 * PI() * s.value / 5400.0)
       + (ABS(CHECKSUM(NEWID())) % 100) / 100.0 - 0.5,

    -- 온도: 같은 주기로 -5~25°C, 배터리보다 살짝 늦게 + ±0.5 흔들림
    10 + 15 * SIN(2 * PI() * s.value / 5400.0 - 0.5)
       + (ABS(CHECKSUM(NEWID())) % 100) / 100.0 - 0.5

FROM GENERATE_SERIES(0, 604799) AS s;   -- 0 ~ 604,799 숫자 줄 만들기
GO
