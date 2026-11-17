# 06. 화면 캡처

## 이번에 직접 실행·캡처하지 못한 이유

- 이 정리는 클라우드 작업 공간에서 했다. WPF 앱은 Windows에서만 실행되고, 사용자 PC의 화면 제어는 허용되지 않은 상태라 **앱을 빌드·실행·캡처하지 않았다**.
- 대신 사용자가 이미 찍은 실제 앱 화면 4장을 `portfolio/screenshots/`에 복사했다. 크기는 약 1150×930이고 요청한 1600×900이 아니다.

## 이미 있는 캡처 (`portfolio/screenshots/`)

| 파일 | 내용 | 원본 | 포폴 사용처 |
| --- | --- | --- | --- |
| `01_main_receiving.png` | 현재 디자인. 상태 배지 "수신 중", 성능 줄(1건/초, 그리기 평균 2.0 ms), 실시간 차트, 1일 과거 기록(빈 시간에서 선 끊김) | 사용자 캡처 2026-11-12 15:41 (1154×935) | 3. 주요 화면 (메인), 5. 트러블슈팅 사례 9 "후" |
| `02_reconnecting.png` | "접속 중 · 재접속 시도 2번째" | `DOCS/images/T-06-4_retrying.png` (1155×942) | 3. 주요 화면 (재접속) |
| `03_disconnected.png` | "끊김 · 대상 컴퓨터에서 연결을 거부 … 3초 후 재접속" | `DOCS/images/T-05-3_disconnected.png` (1158×924) | 3. 주요 화면 (재접속) |
| `04_reconnected_before_gap_fix.png` | 재접속 후 수신 재개. 아래 1일 차트에 빈 시간이 사선으로 이어진 **수정 전** 모습 | `DOCS/images/T-06-4_reconnected.png` (1150×932) | 5. 트러블슈팅 사례 9 "전" |

`02`~`04`는 상태 표시가 테두리 배지로 바뀌기 전 화면이다. 포폴에는 같은 디자인으로 다시 찍는 것을 권장한다.

## 직접 찍을 캡처 목록

공통: 창 크기 1600×900 근처(최대화 후 창 모드로 조절), Windows 배율 100%가 아니면 캡처 크기가 달라짐.

| 번호·파일명 | 화면 | 띄우는 방법 (데이터·버튼) | 포폴 사용처 |
| --- | --- | --- | --- |
| `05_history_week.png` | 1주일 과거 기록 (바퀴별 최고·최저 선 2개) | SSMS에서 `DB/02_seed_week.sql` 실행(최근 1주일 채움, 주의: 수신 데이터 Source=2 삭제) → 앱 실행 → "1주일" 클릭 | 3. 주요 화면, 5. 트러블슈팅 사례 3 |
| `06_history_day.png` | 1일 과거 기록 (5분 평균 파동 약 15개) | 위와 같은 데이터 → "1일" 클릭 | 3. 주요 화면 |
| `07_history_3h.png` | 3시간 과거 기록 (충전↗·방전↘ 파동 2개) | 위와 같은 데이터 → "최근 3시간" 클릭 | 3. 주요 화면 |
| `08_warning.png` | 배터리 경고 상태 (빨간 숫자, "경고: 60 % 미만") | `dotnet run --project GroundLink.Simulator -- --warn` → 앱에서 "시뮬레이터 연결" → 10초 대기 | 3. 주요 화면, test-log T-05-7 근거 |
| `09_perf_100hz_batch_on.png` | 100건/초, "화면 갱신 묶기" 켬 → 상단 성능 줄 | `DB/05_time_milliseconds.sql` 실행 확인 → 시뮬레이터 `-- --hz 100` → 연결 → 30초 대기 | 4. 아키텍처(스레드), test-log T-07-1 |
| `10_perf_100hz_batch_off.png` | 100건/초, 묶기 끔 → 성능 줄 | 09 상태에서 체크박스 해제 → 30초 대기 | 위와 같음 (전후 비교) |
| `11_simulator_console.png` | 시뮬레이터 콘솔 "설정: 1초에 100건", "최근 1초 100건 보냄" | 09와 같은 실행 중 콘솔 창 캡처 | 3. 주요 화면, 트러블슈팅 사례 6 결과 |
| `12_tests_passed.png` | Visual Studio 테스트 탐색기 19개 통과 | 테스트 탐색기 → 모두 실행 | 선택 페이지 (테스트), test-log T-07-2 |
| `13_db_saved_rows.png` | SSMS: 수신 데이터 저장 확인 | `SELECT COUNT(*) FROM dbo.Telemetry WHERE Source = 2;` 연결 전후 결과 | 트러블슈팅 사례 8, test-log T-06-1 |
| `14_settings_error.png` (선택) | "설정 오류" 창 | `bin/Debug/net8.0-windows/appsettings.json` 이름을 잠시 바꾸고 실행 → 복구 | test-log T-07-5 |

## 다이어그램 이미지

- `portfolio/architecture.png`: `05-resume.md`의 Mermaid 다이어그램을 그림으로 변환한 것 (1400px 폭).
