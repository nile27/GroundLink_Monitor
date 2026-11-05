namespace GroundLink.Data;

public static class DbSettings
{
    // 설치 때 복사한 연결 문자열에서 Database만 GroundLink로 바꾼 것
    // TrustServerCertificate=True: SSMS에서 "서버 인증서 신뢰" 체크한 것과 같은 의미
    public const string ConnectionString =
        @"Server=localhost\SQLEXPRESS;Database=GroundLink;Trusted_Connection=True;TrustServerCertificate=True;";
}