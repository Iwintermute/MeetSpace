namespace MeetSpace.Client.Shared.Configuration;

public sealed record ClientRuntimeOptions(
    string SupabaseUrl,
    string SupabaseAnonKey,
    string DefaultRealtimeEndpoint,
    string DefaultDeviceId = "uwp-desktop");