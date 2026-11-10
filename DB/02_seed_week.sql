/*
 * 02. 위성 1개, 1주일치 더미 데이터 (1초 간격, 604,800줄)
 * 기간: 지금부터 7일 전 ~ 지금
 * 다시 실행하면 기존 데이터를 비우고 "지금" 기준으로 다시 넣음
 *
 * 궤도 모델
 *   궤도 주기 : 5,669초 (GOMX-4 실측)
 *   그늘 비율 : 0.31 → 그늘 1,757초, 햇빛 3,912초 (GOMX-4 실측)
 *   위상 기준 : 2026-01-01 00:00:00 (시뮬레이터와 같은 값 사용)
 *
 * 배터리 잔량 (%)
 *   햇빛 85 → 100 (충전), 그늘 100 → 85 (방전)
 *   1바퀴 DoD 15% (LEO 일반 10~40% 범위 안에서 선택)
 *
 * 배터리 온도 (°C)
 *   평균 15, 진폭 ±3 (GOMX-4 실측 진폭 2.5~3.5)
 *   햇빛 12 → 18, 그늘 18 → 12 (코사인 곡선)
 *
 * 잡음 : 잔량 ±0.1, 온도 ±0.2 (가정값)
 * 필요 : SQL Server 2022 이상 (GENERATE_SERIES, LEAST)
 */

USE MonitorDB;
GO

TRUNCATE TABLE dbo.Telemetry;
GO

DECLARE @now      DATETIME2(0) = SYSDATETIME();
DECLARE @epoch    DATETIME2(0) = '2026-01-01T00:00:00';

DECLARE @period   INT   = 5669;    -- 궤도 주기 (초)
DECLARE @sun      INT   = 3912;    -- 햇빛 구간 (초)
DECLARE @eclipse  INT   = 1757;    -- 그늘 구간 (초)

DECLARE @socMax   FLOAT = 100.0;   -- 햇빛 끝 잔량
DECLARE @dod      FLOAT = 15.0;    -- 1바퀴 방전 깊이

DECLARE @tempMean FLOAT = 15.0;
DECLARE @tempAmp  FLOAT = 3.0;

WITH Times AS (
    SELECT DATEADD(SECOND, s.value - 604800, @now) AS RecordedAt
    FROM GENERATE_SERIES(0, 604799) AS s
),
Phases AS (
    -- 궤도 안에서 몇 초째인지 (0 ~ 5668). 0 = 햇빛 시작
    SELECT RecordedAt,
           CAST(DATEDIFF_BIG(SECOND, @epoch, RecordedAt) % @period AS INT) AS Phase
    FROM Times
)
INSERT INTO dbo.Telemetry (SatelliteId, RecordedAt, Battery, Temperature)
SELECT
    'SAT-001',
    RecordedAt,

    ROUND(LEAST(100.0,
        CASE WHEN Phase < @sun
             THEN (@socMax - @dod) + @dod * Phase / @sun              -- 충전
             ELSE @socMax - @dod * (Phase - @sun) / @eclipse          -- 방전
        END
        + (ABS(CHECKSUM(NEWID()) % 21) - 10) / 100.0                  -- ±0.1
    ), 2),

    ROUND(
        CASE WHEN Phase < @sun
             THEN @tempMean - @tempAmp * COS(PI() * Phase / @sun)                 -- 상승
             ELSE @tempMean + @tempAmp * COS(PI() * (Phase - @sun) / @eclipse)    -- 하강
        END
        + (ABS(CHECKSUM(NEWID()) % 41) - 20) / 100.0                  -- ±0.2
    , 2)
FROM Phases;
GO