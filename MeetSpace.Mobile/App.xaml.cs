using MeetSpace.Mobile.Pages;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace MeetSpace.Mobile;

public partial class App : Application
{
	private readonly IServiceProvider _services;
	private Window? _window;

	public App(IServiceProvider services)
	{
		InitializeComponent();
		_services = services ?? throw new ArgumentNullException(nameof(services));
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		_window = new Window();
		_ = NavigateToLoginAsync();
		return _window;
	}

	public Task NavigateToLoginAsync()
	{
		return SetRootAsync(() => _services.GetRequiredService<LoginPage>());
	}

	public Task NavigateToHomeAsync()
	{
		return SetRootAsync(() => _services.GetRequiredService<HomeTabbedPage>());
	}

	private Task SetRootAsync(Func<Page> pageFactory)
	{
		return MainThread.InvokeOnMainThreadAsync(() =>
		{
			if (_window == null)
				return;

			_window.Page = pageFactory();
		});
	}
}
