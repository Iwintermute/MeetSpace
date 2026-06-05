using MeetSpace.Mobile.Services;

namespace MeetSpace.Mobile;

public partial class MainPage : ContentPage
{
	private readonly MeetSpaceMobileRuntime _runtime;
	private bool _initialized;

	public MainPage(MeetSpaceMobileRuntime runtime)
	{
		InitializeComponent();
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		if (!_initialized)
		{
			_initialized = true;
			await RunActionAsync("Initialize runtime", () => _runtime.InitializeAsync());
		}

		RefreshStatus();
	}

	private async void OnSignInClicked(object? sender, EventArgs e)
	{
		var email = EmailEntry.Text?.Trim() ?? string.Empty;
		var password = PasswordEntry.Text ?? string.Empty;

		await RunActionAsync("Sign in", async () =>
		{
			var result = await _runtime.SignInAsync(email, password);
			if (!result.IsAuthenticated)
				throw new InvalidOperationException(result.Message ?? "Authentication failed.");

			AppendLog($"Signed in as {result.Tokens?.Email ?? result.Tokens?.UserId ?? "unknown"}.");
		});
	}

	private async void OnSignUpClicked(object? sender, EventArgs e)
	{
		var email = EmailEntry.Text?.Trim() ?? string.Empty;
		var password = PasswordEntry.Text ?? string.Empty;

		await RunActionAsync("Sign up", async () =>
		{
			var result = await _runtime.SignUpAsync(email, password);
			if (result.RequiresEmailConfirmation)
			{
				AppendLog(result.Message ?? "Email confirmation required.");
				return;
			}

			if (!result.IsAuthenticated)
				throw new InvalidOperationException(result.Message ?? "Registration failed.");

			AppendLog($"Account created and signed in as {result.Tokens?.Email ?? result.Tokens?.UserId ?? "unknown"}.");
		});
	}

	private async void OnConnectClicked(object? sender, EventArgs e)
	{
		await RunActionAsync("Connect realtime", async () =>
		{
			var result = await _runtime.ConnectRealtimeAsync();
			if (result.IsFailure)
				throw new InvalidOperationException(result.Error?.Message ?? "Realtime connection failed.");

			AppendLog("Realtime connected.");
		});
	}

	private async void OnDisconnectClicked(object? sender, EventArgs e)
	{
		await RunActionAsync("Disconnect realtime", async () =>
		{
			await _runtime.DisconnectRealtimeAsync();
			AppendLog("Realtime disconnected.");
		});
	}

	private async void OnSignOutClicked(object? sender, EventArgs e)
	{
		await RunActionAsync("Sign out", async () =>
		{
			await _runtime.SignOutAsync();
			AppendLog("Signed out.");
		});
	}

	private void OnRefreshStatusClicked(object? sender, EventArgs e)
	{
		RefreshStatus();
		AppendLog("Status refreshed.");
	}

	private async Task RunActionAsync(string title, Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (Exception ex)
		{
			AppendLog($"{title} error: {ex.Message}");
			await DisplayAlertAsync("MeetSpace Mobile", ex.Message, "OK");
		}
		finally
		{
			RefreshStatus();
		}
	}

	private void RefreshStatus()
	{
		var auth = _runtime.AuthState;
		var session = _runtime.SessionState;

		AuthStatusLabel.Text = auth.IsAuthenticated
			? $"Auth: ✅ {auth.Email ?? auth.UserId ?? "user"}"
			: "Auth: ❌ not authenticated";

		ConnectionStatusLabel.Text = $"Realtime: {session.ConnectionState}";
	}

	private void AppendLog(string message)
	{
		var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
		if (string.IsNullOrWhiteSpace(LogEditor.Text))
		{
			LogEditor.Text = line;
			return;
		}

		LogEditor.Text = $"{LogEditor.Text}{Environment.NewLine}{line}";
	}
}
