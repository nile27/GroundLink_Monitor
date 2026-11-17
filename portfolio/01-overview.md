# 01. 프로젝트 파악 — GroundLink

> 기준: 2026-11-16에 읽은 작업 폴더(`C:\Users\KOSTA\source\repos\GroundLink`)와 로컬 git 기록(`.git/logs/HEAD`, 커밋 객체).
> 표기: `파일:줄` = 작업 폴더의 현재 파일 줄 번호. **[커밋 전]** = 마지막 커밋(`28a7fa3`)에 없고 작업 폴더에만 있는 코드.

## 1. 3줄 요약

1. 위성 1기의 배터리 잔량·배터리 온도 텔레메트리를 **TCP로 실시간 수신**해 화면에 표시하고 경고 기준과 비교하는 WPF 관제 화면이다. (`README.md:3`, `GroundLink/Network/TelemetryReceiver.cs:69-115`, `GroundLink/Rules/WarningRules.cs:9-27`)
2. 받은 데이터를 **MS SQL Server에 10건씩 트랜잭션으로 저장**하고, 저장된 데이터를 기간별(3시간 / 1일 / 1주일)로 요약해 **과거 기록 차트**로 그린다. (`GroundLink/Data/TelemetryRepository.cs:30-63, 94-166`, `GroundLink/ViewModels/MainViewModel.cs:185-203`)
3. 위성 대신 데이터를 보내는 **C# 콘솔 시뮬레이터**(궤도 모델, 전송 속도 옵션)가 같은 솔루션에 있다. (`GroundLink.Simulator/Program.cs:1-14`)

## 2. 기술 스택과 버전

| 구분 | 내용 | 근거 |
| --- | --- | --- |
| 앱 | .NET 8, WPF (`net8.0-windows`, `UseWPF`), `WinExe` | `GroundLink/GroundLink.csproj:4-8` |
| 시뮬레이터 | .NET 8 콘솔 (`net8.0`, `Exe`) | `GroundLink.Simulator/GroundLink.Simulator.csproj:4-5` |
| 테스트 | .NET 8 (`net8.0-windows`, `UseWPF`) | `GroundLink.Tests/GroundLink.Tests.csproj:5-6` |
| DB | SQL Server 2022 이상 (`GENERATE_SERIES`, `LEAST`, `DATE_BUCKET` 사용) | `DB/02_seed_week.sql:20,44,57`, `GroundLink/Data/TelemetryRepository.cs:106,144` |
| 언어 기능 | record, nullable, implicit usings | `GroundLink/Models/Telemetry.cs`, `GroundLink/GroundLink.csproj:6-7` |

**NuGet 패키지**

| 프로젝트 | 패키지 | 버전 | 근거 |
| --- | --- | --- | --- |
| GroundLink | CommunityToolkit.Mvvm | 8.4.2 | `GroundLink/GroundLink.csproj:12` [커밋 전] |
| GroundLink | Microsoft.Data.SqlClient | 7.1.1 | `GroundLink/GroundLink.csproj:13` |
| GroundLink | ScottPlot.WPF | 5.1.59 | `GroundLink/GroundLink.csproj:14` |
| GroundLink.Tests | Microsoft.NET.Test.Sdk | 17.11.1 | `GroundLink.Tests/GroundLink.Tests.csproj` [커밋 전] |
| GroundLink.Tests | xunit | 2.9.2 | 같은 파일 [커밋 전] |
| GroundLink.Tests | xunit.runner.visualstudio | 2.8.2 | 같은 파일 [커밋 전] |
| GroundLink.Simulator | (없음, 기본 라이브러리만) | - | `GroundLink.Simulator/GroundLink.Simulator.csproj` |

## 3. 폴더·프로젝트 구조

```
GroundLink.sln
├─ GroundLink/                    WPF 앱
│  ├─ MainWindow.xaml(.cs)        View: 화면 배치·바인딩, 차트 그리기 호출, 타이머
│  ├─ ViewModels/MainViewModel.cs ViewModel: 화면 값, 버튼 명령, 수신·저장 처리 [커밋 전]
│  ├─ Models/                     Model: Telemetry, SummaryPoint, HistorySummary (record)
│  ├─ Data/TelemetryRepository.cs 데이터 접근: ADO.NET 조회·저장
│  ├─ Network/TelemetryReceiver.cs 통신: TCP 수신 스레드, 자동 재접속
│  ├─ Rules/WarningRules.cs       규칙: 경고 판정 (순수 함수)
│  ├─ Charts/ChartDrawer.cs       차트: ScottPlot 그리기 (static)
│  ├─ Performance/PerfMonitor.cs  성능 측정값 모으기 [커밋 전]
│  ├─ AppSettings.cs, appsettings.json  실행 설정 [커밋 전]
│  └─ Theme.cs                    화면 색 상수
├─ GroundLink.Simulator/Program.cs  콘솔 시뮬레이터 (최상위 문)
├─ GroundLink.Tests/              xUnit 테스트 3개 파일 [커밋 전]
├─ DB/                            00~05 스크립트 (05는 [커밋 전]), 99_checks.sql
└─ DOCS/                          requirements.md, test-log.md, images/
```

| 역할 | 파일 | 근거 |
| --- | --- | --- |
| View | `MainWindow.xaml`, `MainWindow.xaml.cs` | 바인딩 `MainWindow.xaml:88-247`, 코드 비하인드는 차트·타이머만 `MainWindow.xaml.cs:1-20` 머리말 |
| ViewModel | `MainViewModel` | `ObservableObject` 상속 `MainViewModel.cs:43`, `[ObservableProperty]` `:90-132`, `[RelayCommand]` `:140,165,185` |
| Model | `Telemetry`, `SummaryPoint`, `HistorySummary`, `HistoryChartData` | `Models/*.cs`, `MainViewModel.cs:470` |
| Service 성격 | `TelemetryRepository`(DB), `TelemetryReceiver`(TCP) | 인터페이스 없이 구체 클래스 (`TelemetryRepository.cs:16`, `TelemetryReceiver.cs:19`) |
| 유틸 | `ChartDrawer`, `WarningRules`, `PerfMonitor`, `Theme` | static 클래스 또는 단순 클래스 |

## 4. 아키텍처 패턴

| 항목 | 상태 | 근거 |
| --- | --- | --- |
| MVVM | **작업 폴더 기준 적용됨 [커밋 전]**. 마지막 커밋(`28a7fa3`)은 코드 비하인드 구조 (`ViewModels/` 폴더가 커밋 트리에 없음) | `MainViewModel.cs:43`, `MainWindow.xaml.cs:60` (`DataContext = viewModel`) |
| MVVM 라이브러리 | CommunityToolkit.Mvvm (소스 생성기: `[ObservableProperty]`, `[RelayCommand]`, `[NotifyPropertyChangedFor]`) | `MainViewModel.cs:113-117, 140, 165` |
| DI 컨테이너 | **없음**. 창 생성자에서 `new`로 직접 조립 | `MainWindow.xaml.cs:54-57` |
| 인터페이스 추상화 | **없음**. Repository·Receiver는 구체 클래스를 그대로 받음 | `MainViewModel.cs:77-79` |
| ViewModel의 WPF 의존 | 없음. 색은 `"#RRGGBB"` 문자열, UI 스레드 전환은 `Action<Action> runOnUi`로 받음 | `MainViewModel.cs:37, 56`, `DOCS/requirements.md:209` |
| 차트 | ScottPlot은 바인딩 미지원이라 View 코드 비하인드에서 그림 (ViewModel 이벤트 구독) | `MainWindow.xaml.cs:58-59, 111-141`, `DOCS/requirements.md:208` |
| 데이터 접근 | ADO.NET (`SqlConnection`/`SqlCommand`), ORM 없음, 저장 프로시저 없음 | `TelemetryRepository.cs:30-63` |
| 설정 | `appsettings.json` + `System.Text.Json` (Microsoft.Extensions.Configuration 미사용) | `AppSettings.cs:21-35` [커밋 전] |

## 5. 기간과 커밋

- **저장소 첫 커밋**: `fc1e0db` 2026-11-04 12:13 "feat(monitor): WPF 프로젝트 생성" (`.git/logs/HEAD` 1행)
- 이 저장소에는 **이전 버전(v1)** 기록이 같이 있다. v1 코드는 `a1dafaf`(2026-11-09 14:03, "v1 코드 제거 (v1-archive 태그에 보관)")에서 지워졌고 `v1-archive` 태그로 남아 있다 (`.git/refs/tags/v1-archive`).
- **현재 프로젝트(v2) 첫 커밋**: `96573f8` 2026-11-09 14:33 "v2 시작: 빈 WPF 프로젝트 + DB 스크립트"
- **마지막 커밋**: `28a7fa3` 2026-11-12 14:35 "feat: 중복 저장 방지 인덱스" (`.git/refs/heads/main`, `origin/main`도 같은 해시)
- **v2 커밋 수: 14개** (`96573f8` ~ `28a7fa3`, 로컬 reflog의 최종 브랜치 기준. 중간에 `reset HEAD~2`로 버린 커밋 2개 `5cb870f`, `9641d30`은 제외)
- 저장소 전체 커밋 수: reflog로는 원격에서 merge로 들어온 커밋(`207054e`)의 하위 기록을 셀 수 없어 **확인 못 함**. `git rev-list --count HEAD`로 확인 필요.
- **커밋 안 된 작업 (작업 폴더 vs `28a7fa3` 비교)**: MVVM(`ViewModels/`), 성능 측정(`Performance/`), 설정 파일, 테스트 프로젝트, `README.md`, `DB/05_time_milliseconds.sql`, `DOCS/images/`, 그리고 `MainWindow.xaml(.cs)`·`ChartDrawer.cs`·`TelemetryRepository.cs`·`Program.cs`·`requirements.md`·`test-log.md` 수정, `Data/TelemetryBatchSaver.cs` 삭제. 마지막 파일 수정 시각은 2026-11-12 16:47 (`ChartDrawer.cs`).

**v2 커밋 목록** (시각은 KST, 근거: `.git/logs/HEAD`, 변경 파일은 커밋 트리 비교)

| 해시 | 날짜 | 메시지 | 주요 변경 파일 |
| --- | --- | --- | --- |
| 96573f8 | 11-09 | v2 시작: 빈 WPF 프로젝트 + DB 스크립트 | sln, csproj, DB 00/01/02/99 |
| 763463a | 11-09 | 2단계: DB에서 최신 값 1줄 조회해서 표시 | MainWindow, DB 폴더 이동 |
| 39d4f89 | 11-10 | docs: UI 요구사항 추가 | DOCS/requirements.md |
| b4fdd5e | 11-10 | feat: 과거 기록 차트 | MainWindow, csproj |
| 46c7be6 | 11-10 | feat: 위성 시뮬레이터 추가 | GroundLink.Simulator/* |
| 55ff310 | 11-10 | feat: 더미 데이터를 궤도 모델로 변경 | DB/02_seed_week.sql |
| ae6ab01 | 11-10 | feat: 시뮬레이터를 궤도 모델로 변경 | Simulator/Program.cs |
| 45d530f | 11-10 | docs: 경고 기준과 더미 데이터 근거 추가 | requirements.md |
| a88b761 | 11-10 | 별도 스레드 수신 | DOCS/test-log.md (코드 변경 없음) |
| dd18135 | 11-10 | feat: 실시간 현재 값 표시 + 경고, 차트 한글 글꼴 수정 | MainWindow, test-log |
| 00e9021 | 11-10 | refactor: 기능별 클래스 분리, 실시간 차트, 가독성 개선 | Charts/Data/Models/Network/Rules 추가 |
| 685f099 | 11-11 | feat: 수신 데이터 DB 저장 | DB/03, Repository, TelemetryBatchSaver 등 |
| d54c61c | 11-11 | feat: 자동 재접속 | Theme.cs 추가, Receiver, ChartDrawer, MainWindow |
| 28a7fa3 | 11-12 | feat: 중복 저장 방지 인덱스 | DB/04, DB/01, .gitignore |

> 참고: `a88b761` "별도 스레드 수신"은 메시지와 달리 `DOCS/test-log.md`만 추가했다. 스레드 수신 코드는 다음 커밋 `dd18135`의 `MainWindow.xaml.cs` 변경에 들어 있다 (커밋 트리 비교).
