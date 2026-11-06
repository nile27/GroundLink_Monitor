-- ═════════════════════════════════════════════
-- 차트 조회 인덱스 개선: IX_Telemetry_Satellite_Time 에 Battery 포함 (INCLUDE)
-- 이미 schema.sql로 DB를 만든 경우에 한 번 실행. SSMS에서 새 쿼리 창에 붙여 넣고 F5
--
-- 왜?
--   차트 쿼리는 SatelliteId, RecordedAt으로 찾고 Battery를 읽는다.
--   기존 인덱스엔 Battery가 없어서, 찾은 행마다 원본 테이블에 다시 가서 Battery를 가져와야 했다.
--   24시간(약 86,000행)이면 그 왕복이 너무 많아서, SQL Server는 아예 인덱스를 안 쓰고
--   테이블 전체(864만 행)를 훑는 쪽을 골랐다.
--   Battery를 인덱스 안에 같이 넣어 두면 인덱스만 읽고 끝난다 (커버링 인덱스).
--
-- INCLUDE: 정렬 기준(키)에는 안 넣고 값만 같이 저장 → 키가 커지지 않음
-- DROP_EXISTING = ON: 같은 이름의 인덱스를 지우고 다시 만드는 걸 한 번에
-- 864만 행 기준 수십 초 걸리고, 그동안 이 테이블 쓰기는 잠깐 기다린다. 앱을 끄고 실행 권장
-- ═════════════════════════════════════════════

USE GroundLink;
GO

CREATE INDEX IX_Telemetry_Satellite_Time
    ON dbo.Telemetry (SatelliteId, RecordedAt)
    INCLUDE (Battery)
    WITH (DROP_EXISTING = ON);
GO
