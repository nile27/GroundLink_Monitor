# GroundLink

위성 1기의 배터리 텔레메트리를 실시간으로 받아 보여주고 저장하는 WPF 관제 화면 (학습·포트폴리오용).

![실행 화면](DOCS/images/T-06-4_reconnected.png)

## 기능

- **실시간 수신**: 시뮬레이터(TCP)에서 JSON 한 줄씩 수신, 별도 스레드에서 받고 화면은 UI 스레드에서 갱신
- **현재 값**: 배터리 잔량·배터리 온도, 경고 기준까지 남은 여유, 경고 시 빨간색
- **실시간 차트**: 최근 5분 (ScottPlot)
- **과거 기록**: 3시간·1일은 분 단위 평균 파동, 1주일은 궤도 1바퀴 단위 최고·최저 (SQL `DATE_BUCKET`)
- **저장**: 받은 데이터를 10건씩 묶어 한 트랜잭션으로 저장, 같은 시각 중복은 고유 인덱스로 차단
- **연결 관리**: 상태 배지 (접속 중 / 수신 중 / 끊김), 끊기면 3초마다 자동 재접속
- **성능 측정**: 수신 건수, 차트 그리기 시간, 수신→화면 지연, DB 저장 시간을 1초마다 표시. 화면 갱신 묶기(0.1초) 켜기/끄기

## 구조

```
시뮬레이터 ──TCP──▶ TelemetryReceiver (수신 스레드)
                        │
                        ▼
                  MainViewModel ──저장──▶ TelemetryRepository ──▶ SQL Server
                        │ 값 변경 (바인딩)        ▲ 조회
                        ▼                        │
                  MainWindow.xaml          (과거 기록·최신 값)
                        │ 차트만 코드에서 그림 (ScottPlot은 바인딩 미지원)
                        ▼
                  ChartDrawer
```

| 폴더 / 파일 | 역할 |
| --- | --- |
| `GroundLink/ViewModels/MainViewModel.cs` | 화면 값, 버튼 명령, 수신·저장 처리 (CommunityToolkit.Mvvm) |
| `GroundLink/MainWindow.xaml(.cs)` | 화면 배치와 바인딩, 차트 그리기 호출 |
| `GroundLink/Network/TelemetryReceiver.cs` | TCP 수신, 자동 재접속 |
| `GroundLink/Data/TelemetryRepository.cs` | DB 조회·저장 |
| `GroundLink/Charts/ChartDrawer.cs` | ScottPlot 차트 |
| `GroundLink/Rules/WarningRules.cs` | 경고 판정 |
| `GroundLink/Performance/PerfMonitor.cs` | 성능 측정값 모으기 |
| `GroundLink.Simulator/` | 위성 시뮬레이터 (궤도 모델, 전송 속도 옵션) |
| `GroundLink.Tests/` | 단위 테스트 (xUnit) |
| `DB/` | DB 생성·변경 스크립트 (번호 순서대로 실행) |
| `DOCS/` | 요구사항 정의서, 테스트 기록 |

## 실행

필요: Windows, .NET 8 SDK, SQL Server 2022 이상 (Express 가능, `GENERATE_SERIES`·`DATE_BUCKET` 사용), Visual Studio 2022

1. **DB**: SSMS에서 `DB/00` → `01` → `02` → `03` → `04` → `05` 순서로 실행 (`02`가 1주일치 더미 데이터 604,800줄 생성)
2. **설정**: `GroundLink/appsettings.json`의 연결 문자열을 내 SQL Server에 맞게 수정
3. **시뮬레이터**

   ```bash
   dotnet run --project GroundLink.Simulator              # 1초 1건
   dotnet run --project GroundLink.Simulator -- --hz 100  # 1초 100건 (성능 측정)
   dotnet run --project GroundLink.Simulator -- --warn    # 배터리 경고 값
   ```

4. **앱**: Visual Studio에서 `GroundLink` 실행 → "시뮬레이터 연결"

## 테스트

Visual Studio 테스트 탐색기에서 실행, 또는

```bash
dotnet test GroundLink.Tests
```

수동 테스트 절차와 결과는 [DOCS/test-log.md](DOCS/test-log.md).

## 배포

```bash
dotnet publish GroundLink -c Release -r win-x64 --self-contained false -o publish
```

`publish` 폴더의 `GroundLink.exe`와 `appsettings.json`을 함께 복사해서 실행 (.NET 8 데스크톱 런타임 필요).

## 데이터 모델 근거

궤도 주기 5,669초·그늘 비율 0.31·배터리 온도 진폭은 GOMX-4 실측 값 기준. 경고 기준과 출처는 [DOCS/requirements.md](DOCS/requirements.md).
