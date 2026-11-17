# 03. 스레드·비동기 정리

> 이력서에는 이 영역을 **[개발 중]**으로 표시한다. 아래는 현재 코드(작업 폴더, 대부분 [커밋 전])에서 찾은 사실과, 그 코드의 잠재적 문제다.
> "잠재적 문제"는 코드를 읽고 판단한 것이며 실제로 재현해 본 것이 아니면 **(추정)**으로 표시했다.

## 1. 검색 결과 요약

검색 대상: `GroundLink/`, `GroundLink.Simulator/`, `GroundLink.Tests/` 의 `.cs`

| 항목 | 사용 여부 | 위치 |
| --- | --- | --- |
| `async` / `await` | **없음** | - |
| `Task.Run` | **없음** | - |
| `Thread` (직접 생성) | 있음 | `TelemetryReceiver.cs:48` |
| `Thread.Sleep` | 있음 | `TelemetryReceiver.cs:82`, `Program.cs:124` |
| `BackgroundWorker` | 없음 | - |
| `Dispatcher.InvokeAsync` | 있음 (1곳, ViewModel에 함수로 넘김) | `MainWindow.xaml.cs:57` |
| `Dispatcher.Invoke` / `BeginInvoke` | 없음 | - |
| `SynchronizationContext` | 없음 | - |
| `ConfigureAwait` | 없음 | - |
| `CancellationToken` | **없음** | - |
| `lock` | 있음 | `PerfMonitor.cs:30, 36, 46, 57, 68` |
| `SemaphoreSlim` | 없음 | - |
| `DispatcherTimer` | 있음 (2개) | `MainWindow.xaml.cs:44-45, 64-65, 89-90` |
| 기타 Timer (`System.Threading.Timer` 등) | 없음 | - |
| `ObservableCollection` | **없음** (컬렉션 바인딩 자체가 없음) | - |
| `IProgress<T>` | 없음 | - |
| `ConcurrentQueue` | 있음 | `MainViewModel.cs:63, 243, 252` |
| `Parallel.For` (테스트) | 있음 | `PerfMonitorTests.cs:39` |

## 2. 위치별 분석

### 2-1. 수신 스레드 생성 — `TelemetryReceiver.cs:46-53`

- **하는 일**: `new Thread(ReceiveLoop) { IsBackground = true }`로 수신 전용 스레드를 만들고 바로 반환한다.
- **왜 UI 스레드 문제와 관련 있나**: `ReadLine()`(`:99`)은 데이터가 올 때까지 멈추는 블로킹 호출이다. UI 스레드에서 부르면 창이 멈춘다. 실제로 UI 스레드에서 받게 했을 때 "응답 없음"이 된 기록이 있다 (`test-log.md:120-125`, T-05-1 의도된 실패).
- **지금 코드의 잠재적 문제**
  - **취소 불가**: 루프가 `while (true)`(`:73`)이고 멈추는 수단(`CancellationToken`, `Stop` 메서드)이 없다. 연결 끊기 기능을 만들 수 없는 구조다.
  - **종료 시 정리 없음**: `IsBackground = true`라서 창을 닫으면 스레드가 그대로 끝난다. ViewModel의 저장 대기 리스트(최대 9건, `MainViewModel.cs:50, 59`)를 저장하는 코드가 창 닫기 경로에 없다 → 마지막 몇 건이 DB에 안 남을 수 있음 (추정, 재현 안 함).
  - `Thread.Sleep(3초)`(`:82`)은 수신 스레드만 멈추므로 UI 문제는 없다. 다만 취소할 수 없는 대기다.

### 2-2. 수신 이벤트가 수신 스레드에서 발생 — `TelemetryReceiver.cs:31-37, 76, 81, 106`

- **하는 일**: `Connecting`, `Received`, `Disconnected` 이벤트를 수신 스레드에서 직접 발생시킨다 (주석 `:11`에 명시).
- **왜 문제가 될 수 있나**: 이벤트 받는 쪽이 화면 요소(WPF 컨트롤)를 바로 바꾸면 `InvalidOperationException`(다른 스레드가 소유한 개체)이 난다. WPF 컨트롤은 만든 스레드(UI 스레드)에서만 바꿀 수 있기 때문이다.
- **지금 코드**: 받는 쪽(`MainViewModel.cs:208-305`)이 화면 값을 바꾸기 전에 `runOnUi(...)`로 UI 스레드에 넘긴다 (`:210, 238, 245, 303`). 크로스 스레드 예외 경로는 보이지 않는다.
- **확인 안 한 것**: 수신 스레드에서 화면을 직접 바꿨을 때 예외가 나는지 재현하는 테스트 T-05-5는 미실시 (`test-log.md:151`).

### 2-3. UI 스레드로 넘기기 — `MainWindow.xaml.cs:57` (`action => Dispatcher.InvokeAsync(action)`)

- **하는 일**: ViewModel이 WPF `Dispatcher`를 직접 모르게, "UI 스레드에서 실행하는 방법"을 함수(`Action<Action> runOnUi`)로 받는다 (`MainViewModel.cs:56, 85`). 테스트에서는 `action => action()`을 넘긴다 (`MainViewModelTests.cs` 머리말).
- **`InvokeAsync`를 쓴 이유**: 수신 스레드가 UI 작업이 끝날 때까지 기다리지 않게 하려는 것 (`Invoke`는 끝날 때까지 기다림). 코드 주석에는 이유가 적혀 있지 않다 (추정).
- **잠재적 문제**
  - **UI 지연 누적 (추정)**: "화면 갱신 묶기"를 끄면 1건마다 `InvokeAsync`가 하나씩 쌓인다 (`MainViewModel.cs:245`). 들어오는 속도가 UI 처리 속도보다 빠르면 Dispatcher 대기열이 길어져 화면이 늦어질 수 있다. 측정 도구(수신→화면 지연, `MainViewModel.cs:265`)는 있지만 고속 측정은 미실시 (T-07-1).
  - 결과(`DispatcherOperation`)를 기다리거나 확인하지 않는다. 넘긴 작업 안에서 예외가 나면 어디로 가는지 코드에서 처리하지 않는다 (확인 필요).

### 2-4. 화면 갱신 묶기 큐 — `MainViewModel.cs:63, 242-258`

- **하는 일**: 수신 스레드가 `ConcurrentQueue`에 넣고(`:243`), UI 스레드의 0.1초 `DispatcherTimer`가 `FlushPending()`으로 꺼내(`:252`) 한 번에 반영한다.
- **왜 이렇게 했나**: 여러 스레드가 같은 큐를 쓰므로 스레드 안전한 큐가 필요하다. 1건마다 UI를 갱신하면 초당 UI 작업 수가 수신 건수만큼 늘어난다 (`requirements.md:204` 결정 기록).
- **잠재적 문제**
  - `BatchUiUpdates`(`:131`)는 UI(체크박스 바인딩)가 쓰고 수신 스레드가 읽는다(`:242`). `volatile`이나 `lock` 없이 읽는다. bool 읽기 자체는 쪼개지지 않지만, 바뀐 값이 수신 스레드에 언제 보이는지는 보장되지 않는다 (추정, 실제 문제는 관찰 안 됨).
  - `receiveThreadId`(`:66`)도 수신 스레드가 쓰고(`:221`) UI가 읽는다(`:270`). 표시용이라 영향은 작다.

### 2-5. DB 저장이 수신 스레드에서 동기 실행 — `MainViewModel.cs:224-240, 288-301`

- **하는 일**: 10건 모이면 `repository.InsertBatch(buffer)`를 **수신 스레드에서 바로** 실행한다.
- **UI 영향**: UI 스레드는 막지 않는다.
- **잠재적 문제**
  - **수신 정지**: 저장하는 동안 다음 줄을 읽지 않는다. 지금은 TCP가 받은 데이터를 쌓아 두므로 잃지는 않는다 (추정).
  - **DB 장애 시 지연 (추정)**: 저장이 실패하면 리스트를 비우지 않고(`:233`은 성공 시에만) 다음 건마다 다시 저장을 시도한다. 연결 문자열에 `Connect Timeout`이 없어 SqlClient 기본값(15초)을 기다릴 수 있으므로(`appsettings.json`), DB가 꺼져 있으면 1건마다 오래 멈출 수 있다. 재현 안 함.
  - `buffer`(`:59`)는 `OnReceived`와 `OnDisconnected` 둘 다 같은 수신 스레드에서만 불려서 동시 접근은 없다.

### 2-6. DB 조회가 UI 스레드에서 동기 실행 — `MainViewModel.cs:141-159, 185-203, 316, 329`

- **하는 일**: "최신 값 조회", 기간 버튼(3시간/1일/1주일), 창 시작(`MainWindow.xaml.cs:86-87`)에서 DB를 **UI 스레드에서 동기로** 조회한다.
- **왜 문제가 될 수 있나**: 조회하는 동안 UI 스레드가 다른 일을 못 해서 창이 멈춘다. 1주일 조회는 1초 1건 기준 약 60만 줄을 요약한다 (`DB/02_seed_week.sql:44`).
- **지금 상태**: **UI 멈춤 가능 (추정)**. 조회 시간은 측정한 적 없다 (`requirements.md:179` N-02 "목표는 첫 측정 후 결정").
- **취소 불가**: 기간 버튼을 빠르게 바꿔 눌러도 앞 조회를 취소할 수 없다.

### 2-7. `DispatcherTimer` 2개 — `MainWindow.xaml.cs:44-45, 64-65, 89-90`

- **하는 일**: 0.1초마다 `FlushPending()`, 1초마다 `PublishPerf()`.
- **왜 DispatcherTimer인가**: Tick이 UI 스레드에서 실행되므로 안에서 화면·ViewModel을 바로 다뤄도 된다 (주석 `:43`).
- **잠재적 문제**: UI 스레드가 바쁘면 Tick이 늦게 온다 → 묶기 간격이 0.1초보다 길어질 수 있다 (DispatcherTimer의 일반적 동작). 창을 닫을 때 `Stop()`은 없지만 창과 함께 사라진다 (추정).

### 2-8. `lock` — `PerfMonitor.cs:30-80`

- **하는 일**: 수신 스레드(수신 건수·저장 시간)와 UI 스레드(지연·그리기 시간)가 같은 카운터를 쓰므로 `lock(sync)`으로 감쌌다.
- **확인**: 여러 스레드 동시 기록 테스트가 있다 (`PerfMonitorTests.cs:35-42`, 10,000번 `Parallel.For`). 테스트 실행 기록은 없다 (T-07-2 미실시).
- **잠재적 문제**: 없음으로 판단. `lock` 안에서 다른 `lock`을 잡지 않아 교착(데드락) 경로가 없다.

### 2-9. 차트 그리기 시간 측정 — `MainWindow.xaml.cs:82-83`

- **하는 일**: ScottPlot `RenderManager.RenderFinished` 이벤트에서 `Elapsed`를 받아 기록.
- **이유**: `WpfPlot.Refresh()`는 `InvalidateVisual`만 호출해서 (DLL 확인, `requirements.md:205`) Refresh 앞뒤 Stopwatch로는 실제 그리기 시간을 잴 수 없다.
- **스레드**: 이 이벤트가 UI 스레드에서 오는지는 코드로 확인하지 않았다. WPF 그리기 경로에서 불리므로 UI 스레드일 것 (추정).

### 2-10. 시뮬레이터 — `Program.cs:71-130`

- **하는 일**: 메인 스레드 하나로 `AcceptTcpClient()`(블로킹, `:74`) → 전송 루프 → 끊기면 다시 대기.
- **영향**: 한 번에 클라이언트 1개만 처리한다. 앞 연결이 살아 있으면 새 연결은 대기열에서 기다린다. 앱이 "접속 중"에 멈춰 있다가 갑자기 연결된 현상의 원인 후보 (추정, `test-log.md:68` "시뮬레이터는 한 번에 클라이언트 1개만 처리한다").
- `Thread.Sleep(1)`(`:124`): Windows에서 약 15.6 ms 단위로 쉬어서 정밀한 주기를 못 맞추는 문제를 "밀린 만큼 몰아 보내기"로 처리 (주석 `:81-83`).

## 3. 공부 순서 (쉬운 것부터)

1. **UI 스레드와 Dispatcher**: WPF 컨트롤은 만든 스레드에서만 바꿀 수 있다. `Dispatcher.Invoke` vs `InvokeAsync` 차이.
2. **스레드를 직접 만드는 방법**: `Thread`, `IsBackground`, 블로킹 호출(`ReadLine`, `AcceptTcpClient`)이 무엇인지. 이 프로젝트의 `TelemetryReceiver`.
3. **async/await 기본**: `Task`, `await`가 스레드를 막지 않는다는 뜻, `async void`를 이벤트 처리기에서만 쓰는 이유.
4. **I/O 비동기 API**: `SqlConnection.OpenAsync`, `ExecuteReaderAsync`, `StreamReader.ReadLineAsync`. CPU 작업이 아니면 `Task.Run` 없이 비동기 API를 쓰는 이유.
5. **Task.Run으로 무거운 계산 분리**: 3·4와 구분 (계산 vs 기다림).
6. **취소**: `CancellationTokenSource`, `CancellationToken`, 취소 시 예외(`OperationCanceledException`) 처리.
7. **진행률**: `IProgress<T>` / `Progress<T>`가 UI 스레드로 알아서 넘겨주는 원리 (`SynchronizationContext`).
8. **스레드 안전**: `lock`, `ConcurrentQueue`, `Interlocked`, `volatile`이 각각 무엇을 보장하는지.
9. **컬렉션 동기화**: `ObservableCollection`을 다른 스레드에서 바꾸면 생기는 예외, `BindingOperations.EnableCollectionSynchronization`.
10. **데드락**: UI 스레드에서 `.Result` / `.Wait()`를 부르면 왜 멈추는지, `ConfigureAwait(false)`의 의미.

## 4. 주말 실습 과제

> 정답 코드는 쓰지 않았다. 각 단계는 **목표 → 힌트 → 성공 확인** 순서. 막히면 아래 Microsoft Learn 검색어로 찾아본다.
> 시작 전에 지금 코드를 새 브랜치에 두고(`git switch -c practice/threading`) 빈 상태에서 다시 만든다.

### 과제 A. 과거 기록 조회를 UI 멈춤 없이 (async/await, 취소)

현재: 기간 버튼을 누르면 UI 스레드에서 DB 조회 (`MainViewModel.cs:185-203`).

| 단계 | 목표 | 힌트 | 성공 확인 |
| --- | --- | --- | --- |
| A-1 | 멈춤 재현하기 | Repository 조회 메서드 안에 `WAITFOR DELAY '00:00:03'`을 SQL 앞에 잠깐 넣는다 | 1주일 버튼을 누르고 3초 동안 창 이동·다른 버튼이 안 됨 |
| A-2 | 조회를 비동기로 | `OpenAsync`, `ExecuteReaderAsync`, `ReadAsync`를 쓰는 `Get...Async` 메서드를 만든다. 반환형은 `Task<HistorySummary>` | 컴파일 성공. 아직 화면은 그대로 |
| A-3 | 명령을 비동기로 | CommunityToolkit의 `[RelayCommand]`는 `async Task` 메서드도 받는다 (`AsyncRelayCommand`가 생성됨) | 3초 지연 중에도 창 이동이 됨 |
| A-4 | 로딩 표시 | ViewModel에 "조회 중" bool 속성, XAML에서 글자나 회전 표시를 바인딩 | 조회 중 글자가 보이고 끝나면 사라짐 |
| A-5 | 중복 클릭 막기 | 생성된 명령의 `IsRunning`, 또는 `CanExecute` | 조회 중 기간 버튼이 꺼짐 |
| A-6 | 취소 | 기간을 바꾸면 앞 조회를 취소. `CancellationToken`을 `ExecuteReaderAsync(token)`까지 전달 | 1주일 → 바로 1일을 누르면 1일 차트만 그려지고 1주일 결과가 덮어쓰지 않음. 오류 줄에 취소 예외가 안 뜸 |
| A-7 | 측정 | 조회 시작~끝을 `Stopwatch`로 재서 화면이나 로그에 표시 | 3시간/1일/1주일 각각 ms가 나옴 → test-log N-02에 기록 |

### 과제 B. 수신 스레드를 취소 가능하게 (연결/끊기 토글)

현재: `new Thread` + `while (true)` + `Thread.Sleep`, 멈추는 수단 없음 (`TelemetryReceiver.cs:46-84`).

| 단계 | 목표 | 힌트 | 성공 확인 |
| --- | --- | --- | --- |
| B-1 | 현재 구조 그리기 | 종이에 "누가 어느 스레드에서 무엇을 부르는지" 화살표로 그린다 (Receiver → ViewModel → runOnUi → View) | 이벤트 3개가 어느 스레드에서 오는지 말로 설명 가능 |
| B-2 | 수신 루프를 Task로 | `Thread` 대신 `async Task ReceiveLoopAsync(CancellationToken)`. `TcpClient.ConnectAsync`, `ReadLineAsync(token)`(.NET 7 이상) | 기존처럼 수신·재접속이 됨 |
| B-3 | 기다림도 취소 가능하게 | `Thread.Sleep(3초)` → `Task.Delay(3초, token)` | 다음 단계에서 끊기를 누르면 3초를 기다리지 않고 바로 멈춤 |
| B-4 | 끊기 버튼 | ViewModel에 `CancellationTokenSource`, 연결 명령과 끊기 명령의 `CanExecute`를 서로 반대로 | 연결 ↔ 끊기가 번갈아 켜지고, 끊으면 상태가 "연결 안 됨" |
| B-5 | 남은 데이터 저장 | 끊기·창 닫기(`Closing`) 때 저장 대기 리스트를 저장 | 12건 받고 끊었을 때 DB에 12줄 (`DB/99_checks.sql` 또는 `SELECT COUNT(*) ... WHERE Source = 2`) |
| B-6 | UI 스레드 넘기기 다시 보기 | `runOnUi` 대신 `await` 뒤 이어지는 코드가 어느 스레드에서 실행되는지 확인 (`Environment.CurrentManagedThreadId` 출력) | `ConfigureAwait(false)`를 붙였을 때와 안 붙였을 때 스레드 번호 차이를 설명 가능 |

### Microsoft Learn 검색어

- `Threading model WPF` (UI 스레드, Dispatcher)
- `Dispatcher.InvokeAsync`
- `Task-based asynchronous pattern (TAP)`
- `Asynchronous programming with async and await`
- `Asynchronous programming ADO.NET SqlClient` (`OpenAsync`, `ExecuteReaderAsync`)
- `Cancellation in managed threads`
- `IProgress<T> Interface`
- `ConfigureAwait FAQ` (.NET Blog)
- `BindingOperations.EnableCollectionSynchronization`
- `CommunityToolkit.Mvvm AsyncRelayCommand`
