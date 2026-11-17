/*
 * 05. 기록 시각을 1/1000초 단위로 (DATETIME2(0) → DATETIME2(3))
 *
 * 이유: 1초에 여러 건 받으면(시뮬레이터 --hz 10 이상) 1초 단위 시각이 겹침
 *       → 04의 고유 인덱스(IGNORE_DUP_KEY)가 겹친 줄을 조용히 버림
 * 기존 데이터는 그대로 (1초 단위 값은 .000으로 바뀜)
 * 인덱스가 걸린 칸은 타입을 바꿀 수 없어서: 인덱스 지우기 → 타입 변경 → 인덱스 다시 만들기
 */

USE MonitorDB;
GO

DROP INDEX UX_Telemetry_Sat_Time ON dbo.Telemetry;
GO

ALTER TABLE dbo.Telemetry
    ALTER COLUMN RecordedAt DATETIME2(3) NOT NULL;
GO

CREATE UNIQUE INDEX UX_Telemetry_Sat_Time
    ON dbo.Telemetry (SatelliteId, RecordedAt)
    INCLUDE (Battery, Temperature)
    WITH (IGNORE_DUP_KEY = ON);
GO

-- 확인: RecordedAt 칸이 datetime2, scale 3 이면 성공
SELECT c.name, t.name AS type_name, c.scale
FROM sys.columns AS c
JOIN sys.types AS t ON c.user_type_id = t.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.Telemetry') AND c.name = 'RecordedAt';
GO
