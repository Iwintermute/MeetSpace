namespace MeetSpace.Client.Shared.Configuration;

public sealed record CallRuntimeOptions(
    int ServerPhaseTimeoutSeconds = 15,
    int BridgePhaseTimeoutSeconds = 12,
    int BackgroundSyncIntervalSeconds = 4,
    int ConferenceMembersRefreshIntervalSeconds = 15);

public sealed record ClientRuntimeOptions(
    string SupabaseUrl,
    string SupabaseAnonKey,
    string DefaultRealtimeEndpoint,
    string DefaultDeviceId = "uwp-desktop",
    CallRuntimeOptions? CallRuntime = null);
