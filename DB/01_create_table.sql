-- ═══════════════════════════════════════════
-- 01. 테이블 + 인덱스 만들기
-- 다시 실행하면: 테이블을 지우고 새로 만듦 (데이터도 같이 사라짐)
-- ═══════════════════════════════════════════

USE MonitorDB;
GO

DROP TABLE IF EXISTS dbo.Telemetry;   -- 있으면 지우고
GO

CREATE TABLE dbo.Telemetry (
    Id          BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,  -- 줄 번호 (자동 증가)
    SatelliteId VARCHAR(16)  NOT NULL,                      -- 위성 ID (예: SAT-001)
    RecordedAt  DATETIME2(0) NOT NULL,                      -- 기록 시각 (1초 단위)
    Battery     FLOAT        NOT NULL,                      -- 배터리 (%)
    Temperature FLOAT        NOT NULL                       -- 온도 (°C)
);
GO

-- "이 위성의 이 기간" 조회용 인덱스
-- INCLUDE: 배터리·온도 값도 인덱스 안에 같이 넣어둠
--          → 조회할 때 원래 테이블까지 다시 찾아가지 않아서 빠름
CREATE INDEX IX_Telemetry_Sat_Time
    ON dbo.Telemetry (SatelliteId, RecordedAt)
    INCLUDE (Battery, Temperature);
GO
