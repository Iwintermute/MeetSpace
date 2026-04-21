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
using System.Net.Http;

namespace MeetSpace.Client.Bootstrap;

public static class MeetSpaceHostBuilder
{
    public static MeetSpaceAppHost Build(Action<IServiceCollection>? configureServices = null)
    {
        var options = new ClientRuntimeOptions(
            SupabaseUrl: "https://mtbbcaykjomycovrxdya.supabase.co",
            SupabaseAnonKey: "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im10YmJjYXlram9teWNvdnJ4ZHlhIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzQ5MDkyODUsImV4cCI6MjA5MDQ4NTI4NX0.AKhEpGPBoiLDfUqAu1-MUgvDDrYlw_M0N_wHdXS9Cx4",
            DefaultRealtimeEndpoint: "ws://127.0.0.1:9002",
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