using MeetSpace.Client.App.Abstractions.Auth;
using MeetSpace.Client.App.Calls;
using MeetSpace.Client.App.Conference;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Feature.Conference.Services;
using MeetSpace.Client.Feature.Conference.ViewModels;
using MeetSpace.Client.Infrastructure.Paths;
using MeetSpace.Client.Infrastructure.Storage;
using MeetSpace.Client.Media.Abstractions;
using MeetSpace.Client.Media.Services;
using MeetSpace.Client.Presentation.Navigation;
using MeetSpace.Client.Presentation.ViewModels;
using MeetSpace.Client.Realtime.Abstractions;
using MeetSpace.Client.Realtime.Connection;
using MeetSpace.Client.Realtime.Gateway;
using MeetSpace.Client.Realtime.Serialization;
using MeetSpace.Client.Security.Abstractions;
using MeetSpace.Client.Security.Services;
using MeetSpace.Client.Shared.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MeetSpace.Client.Bootstrap;

public static class MeetSpaceHostBuilder
{
    public static IHost Build()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IClock, SystemClock>();

                services.AddSingleton<SessionStore>();
                services.AddSingleton<ConferenceStore>();
                services.AddSingleton<CallStore>();

                services.AddSingleton<IAuthSessionService, AuthSessionService>();

                services.AddSingleton<IRealtimeConnection, ClientWebSocketConnection>();
                services.AddSingleton<ProtocolJsonSerializer>();
                services.AddSingleton<IRealtimeGateway, RealtimeGateway>();

                services.AddSingleton<PeerAssignedRouter>();
                services.AddSingleton<ConnectionStateRouter>();
                services.AddSingleton<BootstrapWarmupService>();
                services.AddHostedService(sp => sp.GetRequiredService<BootstrapWarmupService>());

                services.AddSingleton<IEncryptionService, Aes256EnvelopeEncryptionService>();

                services.AddSingleton<IMediaEngine, NullMediaEngine>();
                services.AddSingleton<IAudioDeviceService>(sp => (NullMediaEngine)sp.GetRequiredService<IMediaEngine>());
                services.AddSingleton<IVideoDeviceService>(sp => (NullMediaEngine)sp.GetRequiredService<IMediaEngine>());
                services.AddSingleton<IScreenShareService>(sp => (NullMediaEngine)sp.GetRequiredService<IMediaEngine>());

                services.AddSingleton<IAppPaths, AppPaths>();
                services.AddSingleton<JsonFileStorage>();

                services.AddSingleton<IConferenceFeatureClient, ConferenceFeatureClient>();
                services.AddSingleton<ConferenceCoordinator>();

                services.AddSingleton<ShellNavigationStore>();
                services.AddSingleton<IShellViewModelFactory, ShellViewModelFactory>();
                services.AddSingleton<IShellNavigationService, ShellNavigationService>();

                services.AddTransient<ConferencePageViewModel>();
                services.AddTransient<ShellViewModel>();
            })
            .Build();
    }
}