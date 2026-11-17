# 02. 기능 현황표

> 기준: 작업 폴더 현재 코드 (2026-11-16 읽음). 줄 번호는 현재 파일 기준.
> **완성** = 화면 → ViewModel → 데이터/통신까지 코드상 호출 경로가 끝까지 이어진 것. 실제로 돌려 본 결과는 근거 칸에 `DOCS/test-log.md` 테스트 ID로 따로 적었다. **완성이어도 실행 확인이 없는 것은 "실행 미확인"이라고 적었다.**
> 코드 검색 결과: `TODO`, `FIXME`, `NotImplementedException`, 주석 처리된 코드, 빈 메서드는 **없음** (`GroundLink/`, `GroundLink.Simulator/`, `GroundLink.Tests/`, `DB/` 전체 검색).

## 완성

| 기능명 | 설명 | 상태 | 관련 파일 | 근거 |
| --- | --- | --- | --- | --- |
| DB 최신 값 조회 | "최신 값 조회" 버튼·창 시작 시 최신 1건을 현재 값 카드에 표시 | 완성 | `MainWindow.xaml:88`, `MainViewModel.cs:140-159`, `TelemetryRepository.cs:66-91` | 경로: Command 바인딩 → `LoadLatest` → `GetLatest`. 실행: T-02-1 통과 (`test-log.md:77`, 당시는 코드 비하인드 구조) |
| 현재 값 + 경고 판정 | 배터리·온도 숫자, "경고까지 N" 또는 경고 문구, 경고면 빨간색 | 완성 | `MainViewModel.cs:446-463`, `WarningRules.cs:9-27`, `MainWindow.xaml:154-166` | 정상 값 표시는 T-05-6 통과 (`test-log.md:157`). **경고 상태 화면은 실행 미확인** (T-05-7 미실시, `test-log.md:163`) |
| TCP 실시간 수신 | 별도 스레드에서 JSON 한 줄씩 받아 `Telemetry`로 변환 | 완성 | `TelemetryReceiver.cs:46-115`, `MainViewModel.cs:165-176, 218-246` | T-05-2 통과(일부) (`test-log.md:131`) |
| 자동 재접속 | 끊기거나 접속 실패하면 3초 뒤 다시 접속, 시도 횟수 표시 | 완성 | `TelemetryReceiver.cs:69-84`, `MainViewModel.cs:208-212, 288-305` | T-05-3, T-06-4 통과 (`test-log.md:137, 187`), 화면 `screenshots/02`, `03` |
| 연결 상태 배지 | 접속 중(주황)·수신 중(초록)·끊김(빨강), 상세 글자 | 완성 | `MainWindow.xaml:100-121`, `MainViewModel.cs:90-92, 438-443` | T-06-3 통과 (`test-log.md:181`). 테두리 배지 모양은 `screenshots/01`에서 확인 |
| 실시간 차트 | 최근 5분, 1분 폭으로 시작해 5분까지 늘어난 뒤 흘러감, y축 자동 | 완성 | `ChartDrawer.cs:20-21, 37-102`, `MainWindow.xaml.cs:111-120` | `screenshots/01` (1분 폭에서 왼쪽부터 채워진 상태) |
| 수신 데이터 DB 저장 | 10건 모이면 한 연결·한 트랜잭션으로 저장, 끊길 때 남은 것 저장, 저장 실패해도 수신 유지 | 완성 | `MainViewModel.cs:50, 224-240, 288-301`, `TelemetryRepository.cs:30-63` | T-06-1 통과: 12줄 = 10 + 2 (`test-log.md:169`). 실패 시 수신 유지 경로는 실행 미확인 |
| 중복 저장 방지 | (SatelliteId, RecordedAt) 고유 인덱스 + `IGNORE_DUP_KEY` | 완성 | `DB/04_unique_time.sql:19-24` | 커밋 `28a7fa3`. T-06-2 "통과 (결과 화면 미기록)" (`test-log.md:175`) |
| 과거 기록 (3시간·1일) | 1분/5분 평균선 + 최소~최대 띠 + 경고선, 결론 한 줄 | 완성 | `MainViewModel.cs:185-203, 310-320`, `TelemetryRepository.cs:94-129`, `ChartDrawer.cs:107-130` | `screenshots/01` (1일). 기간 버튼 전환 테스트 T-03-2 미실시 (`test-log.md:89`) |
| 과거 기록 (1주일) | 궤도 1바퀴(5,669초) 단위 최고·최저 선 2개, 90% 이상 찬 바퀴만 | 완성 (실행 미확인) | `TelemetryRepository.cs:131-166`, `MainViewModel.cs:323-...`, `ChartDrawer.cs:134-149` | `DATE_BUCKET(SECOND, @period, RecordedAt, 기준점)` `:144`, `HAVING` `:152`. T-07-3 미실시 (`test-log.md:222`) |
| 빈 시간에서 선 끊기 | 구간 간격이 평소의 1.5배를 넘으면 선을 나눠 그림 | 완성 | `ChartDrawer.cs:194-211` | `screenshots/01` 아래 차트에서 조각으로 표시됨. test-log T-07-4는 "미실시"로 남아 있음 (`test-log.md:228`) |
| 기간 버튼 강조 | 선택된 기간 버튼만 강조색 (`Tag` 바인딩 + `DataTrigger`) | 완성 | `MainWindow.xaml:56-65, 199-208`, `MainViewModel.cs:113-121` | `screenshots/01` ("1일" 강조) |
| 화면 갱신 묶기 | 수신 건을 큐에 쌓고 0.1초마다 한 번에 반영, 체크박스로 끄고 켬 | 완성 | `MainViewModel.cs:63, 131, 242-273`, `MainWindow.xaml.cs:44, 64, 89`, `MainWindow.xaml:126-131` | [커밋 전]. 끔/켬 비교 측정은 미실시 (T-07-1, `test-log.md:208`) |
| 성능 측정 표시 | 1초마다 수신 건수, 차트 그리기 횟수·ms, 수신→화면 지연, DB 저장 ms | 완성 | `PerfMonitor.cs:13-94`, `MainViewModel.cs:275-285`, `MainWindow.xaml.cs:45, 65, 82-83` | [커밋 전]. 1건/초 조건 값만 화면에 있음: `screenshots/01` (그리기 2회/초 평균 2.0 ms, 수신→화면 평균 62 ms) |
| 실행 설정 파일 | 연결 문자열·위성 ID·시뮬레이터 주소를 `appsettings.json`에서 읽음, 없으면 오류 창 후 종료 | 완성 (실행 미확인) | `AppSettings.cs:16-35`, `MainWindow.xaml.cs:51, 94-106`, `GroundLink.csproj:17-21` | [커밋 전]. T-07-5 미실시 (`test-log.md:234`) |
| 시뮬레이터 | TCP 서버, 궤도 모델 값, `--hz N`(1~1000건/초), `--warn`(배터리 -40 %p) | 완성 | `Program.cs:22-43, 71-130` | 1건/초 전송 T-04-1·T-04-2·T-04-3 통과 (`test-log.md:95-107`). `--hz`, `--warn`은 [커밋 전], test-log 기록 없음 |
| 단위 테스트 | 경고 판정 12, ViewModel 5, 성능 측정 2 (총 19케이스) | 완성 (실행 미확인) | `GroundLink.Tests/*.cs` | [커밋 전]. T-07-2 미실시 (`test-log.md:215`) |
| DB 스크립트 | DB 생성, 테이블·인덱스, 1주일 더미 604,800줄, Source 칸, 고유 인덱스, 시각 1/1000초 | 완성 | `DB/00~05` | 05는 [커밋 전] |

## 개발 중 / 미구현

| 기능명 | 설명 | 상태 | 관련 파일 | 근거 |
| --- | --- | --- | --- | --- |
| 범례를 차트 밖으로 | 과거 기록 범례가 오른쪽 아래 데이터를 가림 → 차트 아래로 옮기는 수정 | **미구현 (작업 폴더에 반영 안 됨)** | `ChartDrawer.cs:177` | 현재 줄은 `ShowLegend(Alignment.LowerRight)` 그대로. 수정본을 저장했지만 이후 같은 파일이 이전 내용으로 다시 저장됨 (파일 수정 시각 11-12 16:47) |
| `05` 스크립트 재실행 안전화 | `DROP INDEX IF EXISTS`로 바꾼 버전 | **미구현 (작업 폴더에 반영 안 됨)** | `DB/05_time_milliseconds.sql:13` | 현재 줄은 `DROP INDEX UX_Telemetry_Sat_Time ...` (IF EXISTS 없음) |
| 실시간 차트 빈 시간 끊기 | 끊긴 동안을 직선으로 이음 | 미구현 | `ChartDrawer.cs:69-102` | `test-log.md:43` "미해결" |
| 연결 끊기 / 수신 중지 | 연결 버튼은 한 번 누르면 계속 꺼짐, 수신 스레드를 멈추는 메서드 없음 | 미구현 | `MainViewModel.cs:168, 179`, `TelemetryReceiver.cs:73` (`while (true)`) | `TelemetryReceiver`에 `Stop`/취소 수단 없음 |
| 창 닫을 때 남은 데이터 저장 | 저장 대기 중인 최대 9건 | 미구현 | `MainViewModel.cs:50, 59` | `Closing`/`Closed` 처리 코드 없음 (검색 결과 0건). 수신 스레드는 `IsBackground = true` (`TelemetryReceiver.cs:50`) |
| 과거 기록 자동 갱신 | 새로 받은 데이터는 기간 버튼을 다시 눌러야 반영 | 미구현 | `MainViewModel.cs:185-203` | 조회는 버튼·창 시작 때만 호출 (`MainWindow.xaml.cs:87`) |
| 비동기 DB 조회 | 최신 값·과거 기록 조회가 UI 스레드에서 동기 실행 | 미구현 | `MainViewModel.cs:145, 316, 329` | `async`/`await` 사용 0건 (03-threading.md 참고) |
| 경고 표시 화면 확인 | `--warn` 시뮬레이터로 확인 | 개발 중 (코드 있음, 확인 안 함) | `Program.cs:40-41`, `MainViewModel.cs:448-454` | T-05-7 미실시 |
| 성능 비교 측정 | 1/10/100건/초 × 묶기 끔/켬 | 개발 중 (측정 도구 있음, 측정 안 함) | `test-log.md:193-208` | 표가 비어 있음 |
