/*
 * 03. Telemetry에 Source 칸 추가
 *
 * Source : 데이터 출처
 *   1 = 미리 넣은 더미 데이터 (02_seed_week.sql)
 *   2 = 시뮬레이터에서 소켓으로 받은 데이터 (WPF가 저장)
 *
 * 기존 604,800줄은 기본값 1로 채워짐
 */

USE MonitorDB;
GO

ALTER TABLE dbo.Telemetry
    ADD Source TINYINT NOT NULL
        CONSTRAINT DF_Telemetry_Source DEFAULT 1;
GO