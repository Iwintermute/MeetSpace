using MeetSpace.Client.App.Abstractions.Auth;
using MeetSpace.Client.App.Auth;
using MeetSpace.Client.App.Calls;
using MeetSpace.Client.App.Chat;
using MeetSpace.Client.App.Conference;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Infrastructure.Paths;
using MeetSpace.Client.Infrastructure.Storage;
using MeetSpace.Client.Media.Abstractions;
using MeetSpace.Client.Media.Services;
using MeetSpace.Client.Realtime.Abstractions;
using MeetSpace.Client.Realtime.Connection;
using MeetSpace.Client.Realtime.Gateway;
using MeetSpace.Client.Realtime.Rpc;
using MeetSpace.Client.Security.Abstractions;
using MeetSpace.Client.Security.Services;
using MeetSpace.Client.Shared.Abstractions;
using MeetSpace.Client.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net;
using System.Net.Http;

namespace MeetSpace.Client.Bootstrap;

public static class MeetSpaceHostBuilder
{
    private const string DefaultSupabaseUrl = "https://mtbbcaykjomycovrxdya.supabase.co";
    private const string DefaultSupabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im10YmJjYXlram9teWNvdnJ4ZHlhIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzQ5MDkyODUsImV4cCI6MjA5MDQ4NTI4NX0.AKhEpGPBoiLDfUqAu1-MUgvDDrYlw_M0N_wHdXS9Cx4";
    private const string DefaultRealtimeEndpoint = "wss://31.177.83.146:9002";

    private static string ReadConfigValue(params string[] envNames)
    {
        foreach (var envName in envNames)
        {
            if (string.IsNullOrWhiteSpace(envName))
                continue;

            var value = Environment.GetEnvironmentVariable(envName, EnvironmentVariableTarget.Process);
            if (string.IsNullOrWhiteSpace(value))
                value = Environment.GetEnvironmentVariable(envName, EnvironmentVariableTarget.User);
            if (string.IsNullOrWhiteSpace(value))
                value = Environment.GetEnvironmentVariable(envName, EnvironmentVariableTarget.Machine);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    private static string NormalizeRealtimeEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return string.Empty;

        var normalized = endpoint.Trim();
        if (normalized.IndexOf("://", StringComparison.Ordinal) < 0)
            normalized = $"wss://{normalized}";

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            return endpoint.Trim();

        if (string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase) &&
            !IsLoopbackHost(uri.Host))
        {
            var uriBuilder = new UriBuilder(uri)
            {
                Scheme = "wss",
                Port = uri.IsDefaultPort ? -1 : uri.Port
            };
            normalized = uriBuilder.Uri.AbsoluteUri;
        }

        return normalized;
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        var normalizedHost = host.Trim('[', ']');
        if (IPAddress.TryParse(normalizedHost, out var address))
            return IPAddress.IsLoopback(address);

        return false;
    }
    public static MeetSpaceAppHost Build(Action<IServiceCollection>? configureServices = null)
    {
        var resolvedSupabaseUrl = ReadConfigValue("MEETSPACE_SUPABASE_URL", "SUPABASE_URL");
        if (string.IsNullOrWhiteSpace(resolvedSupabaseUrl))
            resolvedSupabaseUrl = DefaultSupabaseUrl;

        var resolvedSupabaseAnonKey = ReadConfigValue("MEETSPACE_SUPABASE_ANON_KEY", "SUPABASE_ANON_KEY");
        if (string.IsNullOrWhiteSpace(resolvedSupabaseAnonKey))
            resolvedSupabaseAnonKey = DefaultSupabaseAnonKey;

        var resolvedRealtimeEndpoint = ReadConfigValue(
            "MEETSPACE_REALTIME_ENDPOINT",
            "MEETSPACE_SIGNALING_ENDPOINT",
            "EDUSPACE_REALTIME_ENDPOINT");
        if (string.IsNullOrWhiteSpace(resolvedRealtimeEndpoint))
            resolvedRealtimeEndpoint = DefaultRealtimeEndpoint;
        resolvedRealtimeEndpoint = NormalizeRealtimeEndpoint(resolvedRealtimeEndpoint);
        var options = new ClientRuntimeOptions(
            SupabaseUrl: resolvedSupabaseUrl,
            SupabaseAnonKey: resolvedSupabaseAnonKey,
            DefaultRealtimeEndpoint: resolvedRealtimeEndpoint,
            DefaultDeviceId: "uwp-desktop",
            CallRuntime: new CallRuntimeOptions(
                ServerPhaseTimeoutSeconds: 15,
                BridgePhaseTimeoutSeconds: 12,
                BackgroundSyncIntervalSeconds: 4,
                ConferenceMembersRefreshIntervalSeconds: 15));

        return Build(options, configureServices);
    }

    public static MeetSpaceAppHost Build(
        ClientRuntimeOptions options,
        Action<IServiceCollection>? configureServices = null)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        var services = new ServiceCollection();

        services.AddSingleton(options);

        services.AddSingleton<IClock, SystemClock>();

        services.AddSingleton<SessionStore>();
        services.AddSingleton<AuthSessionStore>();
        services.AddSingleton<ConferenceStore>();
        services.AddSingleton<CallStore>();
        services.AddSingleton<ChatStore>();

        services.AddSingleton<IAuthSessionService, AuthSessionService>();

        services.AddSingleton<RealtimeSessionService>();
        services.AddSingleton<IRealtimeSessionService>(sp =>
            sp.GetRequiredService<RealtimeSessionService>());

        services.AddSingleton(sp =>
        {
            var baseUrl = options.SupabaseUrl.Trim();
            if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
                baseUrl += "/";

            return new HttpClient
            {
                BaseAddress = new Uri(baseUrl, UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(30)
            };
        });

        services.AddSingleton<ISupabaseAuthClient>(sp =>
            new SupabaseAuthClient(
                sp.GetRequiredService<HttpClient>(),
                options.SupabaseUrl,
                options.SupabaseAnonKey));

        services.AddSingleton<IRealtimeConnection, ClientWebSocketConnection>();
        services.AddSingleton<ProtocolJsonSerializer>();
        services.AddSingleton<IRealtimeGateway, RealtimeGateway>();
        services.AddSingleton<IRealtimeRpcClient, RealtimeRpcClient>();

        services.AddSingleton<SessionInboundRouter>();
        services.AddSingleton<ChatInboundRouter>();
        services.AddSingleton<ConferenceInboundRouter>();
        services.AddSingleton<CallInboundRouter>();
        services.AddSingleton<BootstrapWarmupService>();

        services.AddSingleton<IEncryptionService, Aes256EnvelopeEncryptionService>();

        services.AddSingleton<IMediaEngine, NullMediaEngine>();
        services.AddSingleton<IAudioDeviceService>(sp => (NullMediaEngine)sp.GetRequiredService<IMediaEngine>());
        services.AddSingleton<IVideoDeviceService>(sp => (NullMediaEngine)sp.GetRequiredService<IMediaEngine>());
        services.AddSingleton<IScreenShareService>(sp => (NullMediaEngine)sp.GetRequiredService<IMediaEngine>());

        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<JsonFileStorage>();

        services.AddSingleton<IConferenceFeatureClient, ConferenceFeatureClient>();
        services.AddSingleton<ConferenceCoordinator>();

        services.AddSingleton<IChatFeatureClient, ChatFeatureClient>();
        services.AddSingleton<IDirectChatFeatureClient, DirectChatFeatureClient>();
        services.AddSingleton<IConferenceChatFeatureClient, ConferenceChatFeatureClient>();
        services.AddSingleton<ChatCoordinator>();
        services.AddSingleton<IConferenceMediaFeatureClient, ConferenceMediaFeatureClient>();
        services.AddSingleton<IDirectCallFeatureClient, DirectCallFeatureClient>();

        services.AddSingleton<IMediasoupFeatureClient, MediasoupCallFeatureClient>();
        services.AddSingleton<IAudioCallEngine, NullAudioCallEngine>();
        services.AddSingleton<CallCoordinator>();

        services.AddSingleton<RealtimeStartupService>();

        configureServices?.Invoke(services);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = false,
            ValidateOnBuild = false
        });

        return new MeetSpaceAppHost(provider);
    }
}