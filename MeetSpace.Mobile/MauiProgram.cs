using MeetSpace.Client.App.Auth;
using MeetSpace.Client.App.Calls;
using MeetSpace.Client.App.Chat;
using MeetSpace.Client.App.Conference;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Realtime.Abstractions;
using MeetSpace.Client.Shared.Configuration;
using MeetSpace.Mobile.Pages;
using MeetSpace.Mobile.Services;
using MeetSpace.Mobile.ViewModels;
using Microsoft.Extensions.Logging;

namespace MeetSpace.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif
		builder.Services.AddSingleton<MeetSpaceMobileRuntime>();

		builder.Services.AddTransient(sp =>
		{
			var runtime = sp.GetRequiredService<MeetSpaceMobileRuntime>();
			return new MeetingsHomePageViewModel(
				runtime.GetRequiredService<ConferenceCoordinator>(),
				runtime.GetRequiredService<AuthSessionStore>(),
				runtime.GetRequiredService<RealtimeStartupService>(),
				runtime.GetRequiredService<ClientRuntimeOptions>());
		});

		builder.Services.AddTransient(sp =>
		{
			var runtime = sp.GetRequiredService<MeetSpaceMobileRuntime>();
			return new ChatPageViewModel(
				runtime.GetRequiredService<ChatCoordinator>(),
				runtime.GetRequiredService<IDirectChatFeatureClient>(),
				runtime.GetRequiredService<ChatStore>(),
				runtime.GetRequiredService<SessionStore>(),
				runtime.GetRequiredService<AuthSessionStore>(),
				runtime.GetRequiredService<RealtimeStartupService>(),
				runtime.GetRequiredService<CallCoordinator>(),
				runtime.GetRequiredService<CallStore>(),
				runtime.GetRequiredService<IRealtimeGateway>(),
				runtime.GetRequiredService<ClientRuntimeOptions>());
		});

		builder.Services.AddTransient(sp =>
		{
			var runtime = sp.GetRequiredService<MeetSpaceMobileRuntime>();
			return new ConferenceRoomPageViewModel(
				runtime.GetRequiredService<ChatCoordinator>(),
				runtime.GetRequiredService<ChatStore>(),
				runtime.GetRequiredService<ConferenceCoordinator>(),
				runtime.GetRequiredService<CallCoordinator>(),
				runtime.GetRequiredService<CallStore>(),
				runtime.GetRequiredService<AuthSessionStore>(),
				runtime.GetRequiredService<SessionStore>(),
				runtime.GetRequiredService<RealtimeStartupService>(),
				runtime.GetRequiredService<ClientRuntimeOptions>());
		});

		builder.Services.AddTransient(sp =>
		{
			var runtime = sp.GetRequiredService<MeetSpaceMobileRuntime>();
			return new DirectCallPageViewModel(
				runtime.GetRequiredService<CallCoordinator>(),
				runtime.GetRequiredService<CallStore>(),
				runtime.GetRequiredService<AuthSessionStore>(),
				runtime.GetRequiredService<SessionStore>(),
				runtime.GetRequiredService<RealtimeStartupService>(),
				runtime.GetRequiredService<ClientRuntimeOptions>());
		});

		builder.Services.AddTransient<LoginPage>();
		builder.Services.AddTransient<MeetingsPage>();
		builder.Services.AddTransient<ChatPage>();
		builder.Services.AddTransient<ConferenceRoomPage>();
		builder.Services.AddTransient<DirectCallPage>();
		builder.Services.AddTransient<HomeTabbedPage>();

		return builder.Build();
	}
}
