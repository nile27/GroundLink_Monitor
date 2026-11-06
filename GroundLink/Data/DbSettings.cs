namespace GroundLink.Data;

/// <summary>
/// DB 접속 정보.
/// </summary>
public static class DbSettings
{
    // Server: SQL Server 주소\인스턴스 이름
    // Database: 사용할 DB
    // Trusted_Connection=True: Windows 인증 (지금 로그인한 윈도우 계정으로 접속)
    // TrustServerCertificate=True: SSMS의 "서버 인증서 신뢰" 체크와 같은 의미
    public const string ConnectionString =
        @"Server=localhost\SQLEXPRESS;Database=GroundLink;Trusted_Connection=True;TrustServerCertificate=True;";
}
