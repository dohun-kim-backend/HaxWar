using System.Diagnostics;
using System.Diagnostics.Metrics;
using HexWar.Application.Sessions;
using HexWar.Infrastructure.WebSocket;

namespace HexWar.Server.Diagnostics;

/// <summary>
/// OpenTelemetry 메트릭 전송을 위한 수집용 Meter 클래스입니다.
/// </summary>
public class DiagnosticsMetrics
{
    // Prometheus에서 노출할 Meter 정의
    public static readonly Meter Meter = new("HexWar.Diagnostics", "1.0.0");

    private readonly SessionRegistry _sessionRegistry;
    private readonly ConnectionManager _connectionManager;

    public DiagnosticsMetrics(SessionRegistry sessionRegistry, ConnectionManager connectionManager)
    {
        _sessionRegistry = sessionRegistry;
        _connectionManager = connectionManager;

        // 비동기 Observable 게이지 및 카운터 등록
        Meter.CreateObservableGauge("hexwar.working_set_bytes", () => GetWorkingSetBytes(), "bytes", "서버 프로세스의 Working Set 메모리");
        Meter.CreateObservableGauge("hexwar.private_memory_bytes", () => GetPrivateMemoryBytes(), "bytes", "서버 프로세스의 Private Memory");
        Meter.CreateObservableGauge("hexwar.gc.heap_bytes", () => GetGCHeapBytes(), "bytes", ".NET GC 힙 크기");
        Meter.CreateObservableGauge("hexwar.connections", () => GetConnections(), "connections", "활성 WebSocket 연결 수");
        Meter.CreateObservableGauge("hexwar.memory_per_session_bytes", () => GetMemoryPerSessionBytes(), "bytes", "세션당 평균 메모리");

        // GC 컬렉션 횟수를 Gen 라벨별로 분할하여 전송
        Meter.CreateObservableCounter("hexwar.gc.collections_total", () => GetGCCollections(), "collections", ".NET GC 컬렉션 누적 횟수");

        // 게임 세션 정보를 State 라벨별로 분할하여 전송
        Meter.CreateObservableGauge("hexwar.sessions", () => GetSessions(), "sessions", "게임 세션 수");
    }

    private long GetWorkingSetBytes() => Process.GetCurrentProcess().WorkingSet64;
    private long GetPrivateMemoryBytes() => Process.GetCurrentProcess().PrivateMemorySize64;
    private long GetGCHeapBytes() => GC.GetTotalMemory(false);
    private int GetConnections() => _connectionManager.GetTotalConnectionCount();

    private double GetMemoryPerSessionBytes()
    {
        var sessions = _sessionRegistry.GetActiveSessions();
        return sessions.Count > 0 ? (double)GC.GetTotalMemory(false) / sessions.Count : 0;
    }

    private IEnumerable<Measurement<long>> GetGCCollections()
    {
        return new[]
        {
            new Measurement<long>(GC.CollectionCount(0), new KeyValuePair<string, object?>("gen", "0")),
            new Measurement<long>(GC.CollectionCount(1), new KeyValuePair<string, object?>("gen", "1")),
            new Measurement<long>(GC.CollectionCount(2), new KeyValuePair<string, object?>("gen", "2"))
        };
    }

    private IEnumerable<Measurement<int>> GetSessions()
    {
        var sessions = _sessionRegistry.GetActiveSessions();
        int total = sessions.Count;
        int active = sessions.Count(s => s.CurrentPhase == Domain.Enums.GamePhase.Planning);
        int gameover = sessions.Count(s => s.CurrentPhase == Domain.Enums.GamePhase.GameOver);

        return new[]
        {
            new Measurement<int>(total, new KeyValuePair<string, object?>("state", "total")),
            new Measurement<int>(active, new KeyValuePair<string, object?>("state", "active")),
            new Measurement<int>(gameover, new KeyValuePair<string, object?>("state", "gameover"))
        };
    }
}
