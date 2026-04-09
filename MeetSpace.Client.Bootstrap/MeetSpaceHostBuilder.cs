using MeetSpace.Client.App.Abstractions.Auth;
using MeetSpace.Client.App.Auth;
using MeetSpace.Client.App.Calls;
using MeetSpace.Client.App.Chat;
using MeetSpace.Client.App.Conference;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Infrastructure.Paths;
using MeetSpace.Client.Infrastructure.Storage;
using MeetSpace.Client.Media.Abstractions;
using MeetSpace.Client.Media.Services;
using MeetSpace.Client.Realtime.Abstractions;
using MeetSpace.Client.Realtime.Connection;
using MeetSpace.Client.Realtime.Gateway;
using MeetSpace.Client.Realtime.Serialization;
using MeetSpace.Client.Security.Abstractions;
using MeetSpace.Client.Security.Services;
using MeetSpace.Client.Shared.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;

namespace MeetSpace.Client.Bootstrap
{
    public static class MeetSpaceHostBuilder
    {
        public static MeetSpaceAppHost Build(Action<IServiceCollection>? configureServices = null)
        {
            var services = new ServiceCollection();

            services.AddSingleton<IClock, SystemClock>();

            services.AddSingleton<SessionStore>();
            services.AddSingleton<AuthSessionStore>();
            services.AddSingleton<ConferenceStore>();
            services.AddSingleton<CallStore>();
            services.AddSingleton<ChatStore>();

            services.AddSingleton<IAuthSessionService, AuthSessionService>();

            services.AddSingleton(new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            });

            const string supabaseUrl = "https://mtbbcaykjomycovrxdya.supabase.co";
            const string supabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im10YmJjYXlram9teWNvdnJ4ZHlhIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzQ5MDkyODUsImV4cCI6MjA5MDQ4NTI4NX0.AKhEpGPBoiLDfUqAu1-MUgvDDrYlw_M0N_wHdXS9Cx4";

            services.AddSingleton<ISupabaseAuthClient>(sp =>
                new SupabaseAuthClient(
                    sp.GetRequiredService<HttpClient>(),
                    supabaseUrl,
                    supabaseAnonKey));

            services.AddSingleton<RealtimeAuthBinder>();

            services.AddSingleton<IRealtimeConnection, ClientWebSocketConnection>();
            services.AddSingleton<ProtocolJsonSerializer>();
            services.AddSingleton<IRealtimeGateway, RealtimeGateway>();

            services.AddSingleton<PeerAssignedRouter>();
            services.AddSingleton<ConnectionStateRouter>();
            services.AddSingleton<ChatInboundRouter>();
            services.AddSingleton<ConferenceInboundRouter>();
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
            services.AddSingleton<ChatCoordinator>();

            services.AddSingleton<IMediasoupFeatureClient, MediasoupFeatureClient>();

            // ВАЖНО: это было потеряно
            services.AddSingleton<RealtimeStartupService>();

            // Для звонков тоже лучше сразу держать регистрацию
            //services.AddSingleton<CallInboundRouter>();

            // UWP-специфичные/переопределяемые сервисы
            configureServices?.Invoke(services);

            var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = false,
                ValidateOnBuild = false
            });

            return new MeetSpaceAppHost(provider);
        }
    }
}