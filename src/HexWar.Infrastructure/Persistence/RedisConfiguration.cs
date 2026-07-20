using System;
using StackExchange.Redis;

namespace HexWar.Infrastructure.Persistence;

/// <summary>
/// Redis 연결 설정
/// </summary>
public class RedisConfiguration
{
    public const string SectionName = "Redis";

    /// <summary>연결 문자열 (host:port)</summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>비밀번호 (선택)</summary>
    public string? Password { get; set; }

    /// <summary>데이터베이스 인덱스 (기본 0)</summary>
    public int Database { get; set; } = 0;

    /// <summary>연결 재시도 횟수</summary>
    public int ConnectRetry { get; set; } = 3;

    /// <summary>연결 타임아웃 (ms)</summary>
    public int ConnectTimeout { get; set; } = 5000;

    /// <summary>동기 타임아웃 (ms)</summary>
    public int SyncTimeout { get; set; } = 5000;

    /// <summary>게임 세션 TTL</summary>
    public int GameSessionExpiryMinutes { get; set; } = 60;

    /// <summary>종료된 게임 TTL</summary>
    public int GameOverExpiryMinutes { get; set; } = 5;

    /// <summary>매칭 큐 TTL</summary>
    public int MatchmakingQueueExpiryMinutes { get; set; } = 10;

    /// <summary>풀링된 연결 유지 시간</summary>
    public int PooledConnectionLifetimeMinutes { get; set; } = 30;

    /// <summary>
    /// StackExchange.Redis ConfigurationOptions 생성
    /// </summary>
    public ConfigurationOptions ToConfigurationOptions()
    {
        // StackExchange.Redis에서 제공하는 파서로 콤마로 연결된 문자열을 파싱해 내부 IP 목록 객체로 분리 등록을 지원한다.
        var options = ConfigurationOptions.Parse(ConnectionString);
    
        options.Password = Password;

        // Redis 클러스터는 0번 데이터베이스만 지원하므로, 다중 엔드포인트가 지정된 경우 0으로 자동 교정합니다.
        if (options.EndPoints.Count > 1 && Database != 0)
        {
            Console.WriteLine($"[Warning] Redis Cluster only supports database index 0. Configured database {Database} is overridden to 0.");
            options.DefaultDatabase = 0;
        }
        else
        {
            options.DefaultDatabase = Database;
        }

        options.ConnectRetry = ConnectRetry;
        options.ConnectTimeout = ConnectTimeout;
        options.SyncTimeout = SyncTimeout;
        options.AbortOnConnectFail = false;
        options.AllowAdmin = false;
        options.Ssl = false;
        
        return options;
    }
}