/*
 * 04. 같은 위성·같은 시각 중복 저장 막기
 *
 * 기존 인덱스(IX_Telemetry_Sat_Time)를 고유 인덱스로 교체
 * IGNORE_DUP_KEY = ON: 중복인 줄은 에러 없이 건너뛰고 나머지는 저장
 */

USE MonitorDB;
GO

-- 1. 이미 중복이 있는지 확인 (결과가 0줄이어야 2번이 성공함)
SELECT SatelliteId, RecordedAt, COUNT(*) AS 개수
FROM dbo.Telemetry
GROUP BY SatelliteId, RecordedAt
HAVING COUNT(*) > 1;
GO

-- 2. 기존 인덱스를 지우고 고유 인덱스로 다시 만들기
DROP INDEX IX_Telemetry_Sat_Time ON dbo.Telemetry;

CREATE UNIQUE INDEX UX_Telemetry_Sat_Time
    ON dbo.Telemetry (SatelliteId, RecordedAt)
    INCLUDE (Battery, Temperature)
    WITH (IGNORE_DUP_KEY = ON);
GO