namespace GroundLink;

/*
 * 화면 색 (어두운 관제 화면).
 * 차트(ScottPlot)와 C# 코드가 같이 씀. MainWindow.xaml 리소스와 같은 값.
 *
 * 1. 바탕·카드·테두리
 * 2. 글자 (기본 / 보조)
 * 3. 선 색 (주 선 / 보조 선)
 * 4. 상태 색 (정상 / 주의 / 경고)
 */
public static class Theme
{
    public const string Background = "#0B1220";   // 창 바탕
    public const string Card = "#111A2B";         // 카드·차트 바탕
    public const string Line = "#1F2A3D";         // 테두리·격자

    public const string Text = "#E6EDF7";         // 기본 글자
    public const string Muted = "#94A3B8";        // 보조 글자, 축 눈금

    public const string Accent = "#38BDF8";       // 주 선 (실시간·평균·바퀴별 최고)
    public const string Second = "#A78BFA";       // 보조 선 (바퀴별 최저)

    public const string Ok = "#34D399";           // 정상, 수신 중
    public const string Caution = "#FBBF24";      // 경고 기준선, 접속 중
    public const string Danger = "#F87171";       // 경고 상태, 끊김
}
