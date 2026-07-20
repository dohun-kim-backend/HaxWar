// src/HexWar.Infrastructure/Persistence/RedisGameRoomRepository.cs
namespace HexWar.Infrastructure.Persistence;

using System.Linq;
using System.Text.Json;
using System.Buffers;
using ProtoBuf;
using HexWar.Application.Services;
using HexWar.Domain.Entities;
using HexWar.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

/// <summary>
/// Redis 기반 GameRoom 저장소.
/// 서버 재시작, 다중 인스턴스 환경에서 게임 상태를 공유합니다.
/// </summary>
public class RedisGameRoomRepository : IGameRoomRepository, IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ISubscriber _subscriber;
    private readonly RedisConfiguration _config;
    private readonly ILogger<RedisGameRoomRepository> _logger;

    private static readonly JsonSerializerOptions JsonOptions = DomainJsonOptions.Create();

    public RedisGameRoomRepository(
        RedisConfiguration config,
        ILogger<RedisGameRoomRepository> logger)
    {
        _config = config;
        _logger = logger;

        try
        {
            _redis = ConnectionMultiplexer.Connect(config.ToConfigurationOptions());
            _db = _redis.GetDatabase();
            _subscriber = _redis.GetSubscriber();

            _logger.LogInformation(
                "Redis connected: {Endpoint}, DB: {Db}",
                config.ConnectionString, config.Database);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Redis at {Endpoint}", config.ConnectionString);
            throw;
        }
    }

    public async Task<GameRoom?> GetByIdAsync(string roomId)
    {
        try
        {
            var key = GetRoomKey(roomId);
            var value = await _db.StringGetAsync(key);

            if (value.IsNullOrEmpty) return null;

            var gameRoom = Serializer.Deserialize<GameRoom>(new ReadOnlySpan<byte>((byte[])value!));

            if (gameRoom != null)
            {
                _logger.LogDebug(
                    "Restored GameRoom {RoomId}: Phase={Phase}, Round={Round}, Nodes={NodeCount}",
                    roomId, gameRoom.Phase, gameRoom.CurrentRound, gameRoom.Nodes.Count);
            }

            return gameRoom;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get GameRoom {RoomId} from Redis", roomId);
            return null;
        }
    }

    public async Task SaveAsync(GameRoom gameRoom)
    {
        try
        {
            var key = GetRoomKey(gameRoom.RoomId);

            var writer = new ArrayBufferWriter<byte>();
            Serializer.Serialize(writer, gameRoom);

            _logger.LogDebug(
                "Saving GameRoom {RoomId}: {ByteLength} bytes (Protobuf)",
                gameRoom.RoomId, writer.WrittenCount);

            var expiry = gameRoom.Phase == Domain.Enums.GamePhase.GameOver
                ? TimeSpan.FromMinutes(_config.GameOverExpiryMinutes)
                : TimeSpan.FromMinutes(_config.GameSessionExpiryMinutes);

            await _db.StringSetAsync(key, writer.WrittenMemory, expiry);

            // Sorted Set (ZSET) 방식을 이용한 활성 게임방 목록 관리 (클러스터 호환 및 N+1 쿼리 방지)
            if (gameRoom.Phase == Domain.Enums.GamePhase.GameOver)
            {
                await _db.SortedSetRemoveAsync("active_rooms", gameRoom.RoomId);
            }
            else
            {
                var score = DateTimeOffset.UtcNow.Add(expiry).ToUnixTimeSeconds();
                await _db.SortedSetAddAsync("active_rooms", gameRoom.RoomId, score);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize/save GameRoom {RoomId}", gameRoom.RoomId);
        }
    }


    public async Task<bool> ExistsAsync(string roomId)
    {
        try
        {
            return await _db.KeyExistsAsync(GetRoomKey(roomId));
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// 플레이어 세션 정보 저장
    /// </summary>
    public async Task SavePlayerSessionAsync(string playerId, string roomId, string playerSide)
    {
        var sessionInfo = new PlayerSessionInfo
        {
            RoomId = roomId,
            PlayerSide = playerSide,
            ServerId = ServerIdentity.Id,
            ConnectedAt = DateTime.UtcNow
        };

        var writer = new ArrayBufferWriter<byte>();
        Serializer.Serialize(writer, sessionInfo);

        await _db.StringSetAsync(
            $"player:{playerId}",
            writer.WrittenMemory,
            TimeSpan.FromMinutes(30)); // 30분 TTL
    }

    /// <summary>
    /// 플레이어가 속한 게임 찾기
    /// </summary>
    public async Task<string?> FindRoomByPlayerAsync(string playerId)
    {
        var value = await _db.StringGetAsync($"player:{playerId}");
        if (value.IsNullOrEmpty) return null;

        var session = Serializer.Deserialize<PlayerSessionInfo>(new ReadOnlySpan<byte>((byte[])value!));
        return session?.RoomId;
    }


    /// <summary>
    /// 활성 게임방 목록 조회
    /// </summary>
    public async Task<List<string>> GetActiveRoomIdsAsync(int limit = 100)
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // 1. 만료된 방 데이터 벌크 삭제 (O(log(N) + M) 단일 연산으로 클러스터 부하 및 지연 극적 감소)
            // 
            await _db.SortedSetRemoveRangeByScoreAsync("active_rooms", double.NegativeInfinity, now);

            // 2. 최대 limit 개수만큼만 활성 방 ID 조회
            var members = await _db.SortedSetRangeByRankAsync("active_rooms", 0, limit - 1);

            return members.Select(m => m.ToString()).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active room IDs from Redis Sorted Set");
            return new List<string>();
        }
    }

    /// <summary>
    /// Pub/Sub 이벤트 발행 (분산 서버 간 통신)
    /// </summary>
    public async Task PublishGameEventAsync(string roomId, string eventJson)
    {
        await _subscriber.PublishAsync(
            new RedisChannel($"game_events:{roomId}", RedisChannel.PatternMode.Literal),
            eventJson);
    }

    /// <summary>
    /// Pub/Sub 이벤트 구독
    /// </summary>
    public void SubscribeToGameEvents(string roomId, Action<string> handler)
    {
        _subscriber.Subscribe(
            new RedisChannel($"game_events:{roomId}", RedisChannel.PatternMode.Literal),
            (channel, message) => handler(message!));
    }

    // ============================================================
    // 헬퍼
    // ============================================================

    private static string GetRoomKey(string roomId) => $"gameroom:{roomId}";
    private static string GetRoomMetaKey(string roomId) => $"gameroom:{roomId}:meta";

    public void Dispose()
    {
        _redis?.Dispose();
    }
}

[ProtoContract]
public class PlayerSessionInfo
{
    [ProtoMember(1)]
    public string RoomId { get; set; } = string.Empty;

    [ProtoMember(2)]
    public string PlayerSide { get; set; } = string.Empty;

    [ProtoMember(3)]
    public string ServerId { get; set; } = string.Empty;

    [ProtoMember(4)]
    public DateTime ConnectedAt { get; set; }
}