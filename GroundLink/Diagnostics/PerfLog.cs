using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace GroundLink.Diagnostics;

/// <summary>
/// 개발용 성능 측정 도구.
/// [Conditional("DEBUG")]가 붙은 메서드는 Release 빌드에서는 호출 자체가 사라진다.
/// 결과는 실행 폴더(bin\Debug\net8.0-windows)의 measure.log에 쌓인다.
///
/// 측정 코드가 앱 곳곳에 흩어지지 않도록 이 파일에만 둔다.
/// </summary>
public static class PerfLog
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "measure.log");

    /// <summary>출력 창 + measure.log 파일에 한 줄 기록</summary>
    [Conditional("DEBUG")]
    public static void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} {message}";
        Debug.WriteLine(line);
        File.AppendAllText(LogPath, line + Environment.NewLine);
    }

    /// <summary>
    /// UI 지연 측정: 100ms마다 울려야 할 타이머가 실제로 얼마나 늦게 울리는지 잰다.
    /// UI 스레드가 바쁘면 늦어지므로, "화면 버벅임"을 숫자로 볼 수 있다.
    /// 5초마다 최댓값을 기록.
    /// </summary>
    [Conditional("DEBUG")]
    public static void StartUiLagProbe()
    {
        var probe = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        var last = Stopwatch.StartNew();
        long maxLag = 0;
        int ticks = 0;

        probe.Tick += (_, _) =>
        {
            long lag = last.ElapsedMilliseconds - 100;
            last.Restart();
            maxLag = Math.Max(maxLag, lag);

            if (++ticks % 50 == 0) // 100ms × 50 = 5초
            {
                Write($"[측정] 최근 5초 최대 UI 지연: {maxLag} ms");
                maxLag = 0;
            }
        };
        probe.Start();
    }

    // ───────── 앱 시작 측정 ─────────

    private static readonly Stopwatch StartupWatch = new();

    /// <summary>앱 시작 시간 재기 시작 (OnStartup 맨 앞에서 호출)</summary>
    [Conditional("DEBUG")]
    public static void StartStartupTimer() => StartupWatch.Restart();

    /// <summary>
    /// 창이 처음 그려진 시각(첫 프레임)과, 그 뒤 UI가 한가해진 시각(화면 준비 완료) + 메모리를 기록.
    /// 창을 Show() 하기 전에 호출해야 ContentRendered를 놓치지 않는다.
    /// </summary>
    [Conditional("DEBUG")]
    /// <param name="source">데이터 출처 설명 (예: "가짜 위성 10대", "서버")</param>
    public static void TrackStartup(Window window, string source)
    {
        window.ContentRendered += (_, _) =>
        {
            Write($"[측정] {source} / 첫 프레임: {StartupWatch.ElapsedMilliseconds} ms");

            // ApplicationIdle: 그리기·바인딩 등 할 일을 다 끝내고 한가해졌을 때 실행됨
            window.Dispatcher.BeginInvoke(() =>
            {
                var mb = Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;
                Write($"[측정] 화면 준비 완료: {StartupWatch.ElapsedMilliseconds} ms / 메모리: {mb} MB");
            }, DispatcherPriority.ApplicationIdle);
        };
    }

    // ───────── DB 저장 측정 ─────────
    // TelemetryRecorder는 await 뒤 UI 스레드로 돌아오므로 아래 값은 한 스레드에서만 바뀐다

    private static readonly List<double> DbSaveDurations = new();
    private static int _dbInFlight;     // 지금 진행 중인 저장 개수
    private static int _dbMaxInFlight;  // 동시에 진행된 최대 개수 (1보다 크면 1초 안에 못 끝냈다는 뜻)

    /// <summary>DB 저장 시작 표시</summary>
    [Conditional("DEBUG")]
    public static void DbSaveStarted()
    {
        _dbInFlight++;
        _dbMaxInFlight = Math.Max(_dbMaxInFlight, _dbInFlight);
    }

    /// <summary>DB 저장 끝. 10번마다 평균·최대 시간과 동시 진행 최대를 기록</summary>
    /// <param name="recordCount">이번에 저장한 건수</param>
    /// <param name="startTimestamp">저장 시작 때 Stopwatch.GetTimestamp() 값</param>
    [Conditional("DEBUG")]
    public static void DbSaveFinished(int recordCount, long startTimestamp)
    {
        _dbInFlight--;
        DbSaveDurations.Add(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);

        if (DbSaveDurations.Count == 10)
        {
            Write($"[DB] {recordCount}건 저장 / 평균 {DbSaveDurations.Average():F0} ms / 최대 {DbSaveDurations.Max():F0} ms / 동시 진행 최대 {_dbMaxInFlight}");
            DbSaveDurations.Clear();
            _dbMaxInFlight = 0;
        }
    }
}
