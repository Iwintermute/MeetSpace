using MeetSpace.Mobile.Services;

namespace MeetSpace.Mobile.Pages;

public partial class LoginPage : ContentPage
{
	private readonly MeetSpaceMobileRuntime _runtime;
	private bool _initialized;
	private bool _navigated;
	private bool _passwordVisible;

	public LoginPage(MeetSpaceMobileRuntime runtime)
	{
		InitializeComponent();
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		SetBusy(false);

		if (!_initialized)
		{
			_initialized = true;
			await RunBusyAsync("Инициализация…", () => _runtime.InitializeAsync());
		}

		if (_runtime.AuthState.IsAuthenticated)
			await NavigateToHomeAsync();
	}

	private async void SignInButton_Clicked(object? sender, EventArgs e)
	{
		if (!TryGetCredentials(out var email, out var password, out var validationError))
		{
			StatusLabel.Text = validationError;
			await DisplayAlertAsync("MeetSpace", validationError, "OK");
			return;
		}

		await RunBusyAsync("Авторизация…", async () =>
		{
			var result = await _runtime.SignInAsync(email, password);
			if (!result.IsAuthenticated)
			{
				if (IsInvalidCredentials(result.Message))
				{
					StatusLabel.Text = "Аккаунт не найден. Создаём нового пользователя…";
					var signUpResult = await _runtime.SignUpAsync(email, password);
					if (signUpResult.RequiresEmailConfirmation)
					{
						StatusLabel.Text = signUpResult.Message ?? "Подтвердите email и повторите вход.";
						return;
					}

					if (!signUpResult.IsAuthenticated)
					{
						throw new InvalidOperationException(
							signUpResult.Message ?? "Не удалось создать аккаунт.");
					}
				}
				else
				{
					throw new InvalidOperationException(result.Message ?? "Не удалось выполнить вход.");
				}
			}

			var connectResult = await _runtime.ConnectRealtimeAsync();
			if (connectResult.IsFailure)
				throw new InvalidOperationException(connectResult.Error?.Message ?? "Realtime подключение не удалось.");

			await NavigateToHomeAsync();
		});
	}
	private void RevealPasswordButton_Clicked(object? sender, EventArgs e)
	{
		_passwordVisible = !_passwordVisible;
		PasswordEntry.IsPassword = !_passwordVisible;
		RevealPasswordButton.Text = _passwordVisible ? "🙈" : "👁";
	}

	private async void SignUpButton_Clicked(object? sender, EventArgs e)
	{
		if (!TryGetCredentials(out var email, out var password, out var validationError))
		{
			StatusLabel.Text = validationError;
			await DisplayAlertAsync("MeetSpace", validationError, "OK");
			return;
		}

		await RunBusyAsync("Создание аккаунта…", async () =>
		{
			var result = await _runtime.SignUpAsync(email, password);
			if (result.RequiresEmailConfirmation)
			{
				StatusLabel.Text = result.Message ?? "Подтвердите email и войдите снова.";
				return;
			}

			if (!result.IsAuthenticated)
				throw new InvalidOperationException(result.Message ?? "Не удалось создать аккаунт.");

			var connectResult = await _runtime.ConnectRealtimeAsync();
			if (connectResult.IsFailure)
				throw new InvalidOperationException(connectResult.Error?.Message ?? "Realtime подключение не удалось.");

			await NavigateToHomeAsync();
		});
	}

	private async Task RunBusyAsync(string status, Func<Task> action)
	{
		if (BusyIndicator.IsRunning)
			return;
		SetBusy(true);
		BusyIndicator.IsRunning = true;
		StatusLabel.Text = status;
		try
		{
			await action();
		}
		catch (Exception ex)
		{
			StatusLabel.Text = ex.Message;
			await DisplayAlertAsync("MeetSpace", ex.Message, "OK");
		}
		finally
		{
			SetBusy(false);
		}
	}

	private bool TryGetCredentials(out string email, out string password, out string error)
	{
		email = EmailEntry.Text?.Trim() ?? string.Empty;
		password = PasswordEntry.Text ?? string.Empty;
		error = string.Empty;

		if (string.IsNullOrWhiteSpace(email))
		{
			error = "Введите email.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(password))
		{
			error = "Введите пароль.";
			return false;
		}

		return true;
	}

	private void SetBusy(bool busy)
	{
		BusyIndicator.IsRunning = busy;
		EmailEntry.IsEnabled = !busy;
		PasswordEntry.IsEnabled = !busy;
		SignInButton.IsEnabled = !busy;
		SignUpButton.IsEnabled = !busy;
		RevealPasswordButton.IsEnabled = !busy;
	}

	private static bool IsInvalidCredentials(string? message)
	{
		return !string.IsNullOrWhiteSpace(message) &&
			message.IndexOf("Invalid login credentials", StringComparison.OrdinalIgnoreCase) >= 0;
	}
	private Task NavigateToHomeAsync()
	{
		if (_navigated)
			return Task.CompletedTask;

		_navigated = true;
		if (Application.Current is App app)
			return app.NavigateToHomeAsync();

		return Task.CompletedTask;
	}
}
