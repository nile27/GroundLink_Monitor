-- ═════════════════════════════════════════════
-- GroundLink DB 생성 스크립트 (SQL Server 2025 Express 기준)
-- SSMS에서 새 쿼리 창에 붙여 넣고 F5
-- ═════════════════════════════════════════════

CREATE DATABASE GroundLink;
GO

USE GroundLink;
GO

-- 텔레메트리: 1초마다 위성마다 한 줄
CREATE TABLE dbo.Telemetry (
    Id          BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY, -- 자동 증가 번호
    SatelliteId VARCHAR(16)   NOT NULL,                    -- 'SAT-0001'
    RecordedAt  DATETIME2(3)  NOT NULL,                    -- 밀리초까지
    Battery     FLOAT         NOT NULL,                    -- %
    Temperature FLOAT         NOT NULL,                    -- °C
    Mode        VARCHAR(8)    NOT NULL,                    -- 'NORMAL' / 'SAFE'
    InContact   BIT           NOT NULL                     -- 0 / 1
);

-- 차트 조회용: "위성 하나의 특정 기간" 조회를 빠르게
-- INCLUDE (Battery): 차트가 읽는 Battery까지 인덱스에 담아, 원본 테이블을 다시 읽지 않게 함
-- (기존 DB는 002_telemetry_index_include_battery.sql 로 변경)
CREATE INDEX IX_Telemetry_Satellite_Time
    ON dbo.Telemetry (SatelliteId, RecordedAt)
    INCLUDE (Battery);

-- 알람
CREATE TABLE dbo.Alarm (
    Id          BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SatelliteId VARCHAR(16)    NOT NULL,
    RaisedAt    DATETIME2(3)   NOT NULL,
    Message     NVARCHAR(200)  NOT NULL                    -- 한글이라 NVARCHAR
);

-- 최신 알람 조회용
CREATE INDEX IX_Alarm_RaisedAt ON dbo.Alarm (RaisedAt DESC);
GO
