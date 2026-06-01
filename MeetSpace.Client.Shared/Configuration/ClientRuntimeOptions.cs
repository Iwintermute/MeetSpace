namespace MeetSpace.Client.Shared.Configuration;

public sealed record IceServerEntry(
    string Urls,
    string? Username = null,
    string? Credential = null);

public sealed record ReconnectPolicy(
    int InitialDelayMs = 1000,
    int MaxDelayMs = 16000,
    double BackoffMultiplier = 2.0,
    double JitterFactor = 0.2,
    int MaxAttempts = 10);

public sealed record CallRuntimeOptions(
    int ServerPhaseTimeoutSeconds = 15,
    int BridgePhaseTimeoutSeconds = 12,
    int BackgroundSyncIntervalSeconds = 8,
    int ConferenceMembersRefreshIntervalSeconds = 15,
    int AdaptiveQualityCheckIntervalSeconds = 5,
    int LowBandwidthThresholdKbps = 150,
    int CriticalBandwidthThresholdKbps = 50);

public sealed record ClientRuntimeOptions(
    string SupabaseUrl,
    string SupabaseAnonKey,
    string DefaultRealtimeEndpoint,
    string DefaultDeviceId = "uwp-desktop",
    string? MediaAuthToken = null,
    IReadOnlyList<IceServerEntry>? IceServers = null,
    ReconnectPolicy? Reconnect = null,
    CallRuntimeOptions? CallRuntime = null);
