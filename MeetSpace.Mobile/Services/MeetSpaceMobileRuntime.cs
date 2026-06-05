using System.IO;
using MeetSpace.Client.App.Auth;
using MeetSpace.Client.App.Calls;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Bootstrap;
using MeetSpace.Client.Infrastructure.Paths;
using MeetSpace.Client.Infrastructure.Storage;
using MeetSpace.Client.Shared.Results;
using Microsoft.Extensions.DependencyInjection;

namespace MeetSpace.Mobile.Services;

public sealed class MeetSpaceMobileRuntime : IAsyncDisposable
{
	private const string AuthSessionFileName = "auth-session.json";

	private readonly MeetSpaceAppHost _host;
	private readonly SemaphoreSlim _initializeLock = new(1, 1);
	private bool _initialized;
	private bool _disposed;

	public MeetSpaceMobileRuntime()
	{
		_host = MeetSpaceHostBuilder.Build(services =>
		{
			services.AddSingleton<IAudioCallEngine, WebViewAudioCallEngine>();
		});
		var authStore = Services.GetRequiredService<AuthSessionStore>();
		authStore.StateChanged += AuthStore_StateChanged;
	}

	public IServiceProvider Services => _host.Services;
	public T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();
	public AuthSessionState AuthState => Services.GetRequiredService<AuthSessionStore>().Current;
	public SessionState SessionState => Services.GetRequiredService<SessionStore>().Current;

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		EnsureNotDisposed();

		await _initializeLock.WaitAsync(cancellationToken);
		try
		{
			if (_initialized)
				return;

			await _host.StartAsync(cancellationToken);
			_initialized = true;
		}
		finally
		{
			_initializeLock.Release();
		}

		var isAuthenticated = await RestoreAuthSessionAsync(cancellationToken);
		if (isAuthenticated)
			_ = TryConnectRealtimeForAuthenticatedUserAsync();
	}

	public async Task<AuthResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
	{
		EnsureInitialized();

		var authClient = Services.GetRequiredService<ISupabaseAuthClient>();
		var authStore = Services.GetRequiredService<AuthSessionStore>();

		var result = await authClient.SignInAsync(email, password, cancellationToken);
		if (result.IsAuthenticated && result.Tokens != null)
			authStore.SetSession(result.Tokens);

		return result;
	}

	public async Task<AuthResult> SignUpAsync(string email, string password, CancellationToken cancellationToken = default)
	{
		EnsureInitialized();

		var authClient = Services.GetRequiredService<ISupabaseAuthClient>();
		var authStore = Services.GetRequiredService<AuthSessionStore>();

		var result = await authClient.SignUpAsync(email, password, cancellationToken);
		if (result.IsAuthenticated && result.Tokens != null)
			authStore.SetSession(result.Tokens);

		return result;
	}

	public async Task<Result> ConnectRealtimeAsync(CancellationToken cancellationToken = default)
	{
		EnsureInitialized();
		var startupService = Services.GetRequiredService<RealtimeStartupService>();
		return await startupService.EnsureConnectedAsync(cancellationToken: cancellationToken);
	}

	public async Task DisconnectRealtimeAsync(CancellationToken cancellationToken = default)
	{
		EnsureInitialized();
		var startupService = Services.GetRequiredService<RealtimeStartupService>();
		await startupService.DisconnectAsync(cancellationToken);
	}

	public async Task SignOutAsync(CancellationToken cancellationToken = default)
	{
		EnsureInitialized();

		var authStore = Services.GetRequiredService<AuthSessionStore>();
		var authClient = Services.GetRequiredService<ISupabaseAuthClient>();
		var accessToken = NormalizeAuthValue(authStore.Current.AccessToken);

		if (!string.IsNullOrWhiteSpace(accessToken))
		{
			try
			{
				await authClient.SignOutAsync(accessToken, cancellationToken);
			}
			catch
			{
			}
		}

		authStore.ClearSession();
		await SafeDeleteAuthFileAsync(GetAuthSessionFilePath());
	}

	private async Task<bool> RestoreAuthSessionAsync(CancellationToken cancellationToken = default)
	{
		var storage = Services.GetRequiredService<JsonFileStorage>();
		var authStore = Services.GetRequiredService<AuthSessionStore>();
		var authClient = Services.GetRequiredService<ISupabaseAuthClient>();

		var filePath = GetAuthSessionFilePath();

		AuthTokens? persistedTokens;
		try
		{
			persistedTokens = await storage.LoadAsync<AuthTokens>(filePath, cancellationToken);
		}
		catch
		{
			authStore.ClearSession();
			await SafeDeleteAuthFileAsync(filePath);
			return false;
		}

		if (persistedTokens == null)
		{
			authStore.ClearSession();
			return false;
		}

		var accessToken = NormalizeAuthValue(persistedTokens.AccessToken);
		var refreshToken = NormalizeAuthValue(persistedTokens.RefreshToken);
		var userId = NormalizeAuthValue(persistedTokens.UserId);
		var email = NormalizeAuthValue(persistedTokens.Email);

		if (string.IsNullOrWhiteSpace(accessToken) ||
			string.IsNullOrWhiteSpace(refreshToken) ||
			string.IsNullOrWhiteSpace(userId))
		{
			authStore.ClearSession();
			await SafeDeleteAuthFileAsync(filePath);
			return false;
		}

		var effectiveTokens = new AuthTokens(
			accessToken,
			refreshToken,
			userId,
			email,
			persistedTokens.ExpiresAtUtc);
		var nowUtc = DateTimeOffset.UtcNow;

		var needsRefresh =
			!effectiveTokens.ExpiresAtUtc.HasValue ||
			effectiveTokens.ExpiresAtUtc.Value <= nowUtc.AddMinutes(1);

		if (needsRefresh)
		{
			try
			{
				effectiveTokens = await authClient.RefreshAsync(effectiveTokens.RefreshToken, cancellationToken);
				authStore.SetSession(effectiveTokens);
				await storage.SaveAsync(filePath, effectiveTokens, cancellationToken);
				return true;
			}
			catch
			{
				authStore.ClearSession();
				await SafeDeleteAuthFileAsync(filePath);
				return false;
			}
		}

		authStore.SetSession(effectiveTokens);
		return true;
	}

	private async Task TryConnectRealtimeForAuthenticatedUserAsync()
	{
		try
		{
			var authStore = Services.GetRequiredService<AuthSessionStore>();
			if (!authStore.Current.IsAuthenticated)
				return;

			var startupService = Services.GetRequiredService<RealtimeStartupService>();
			await startupService.EnsureConnectedAsync();
		}
		catch
		{
		}
	}

	private string GetAuthSessionFilePath()
	{
		var paths = Services.GetRequiredService<IAppPaths>();
		return Path.Combine(paths.SettingsDirectory, AuthSessionFileName);
	}

	private async void AuthStore_StateChanged(object? sender, AuthSessionState state)
	{
		try
		{
			var storage = Services.GetRequiredService<JsonFileStorage>();
			var filePath = GetAuthSessionFilePath();

			if (!state.IsAuthenticated ||
				string.IsNullOrWhiteSpace(state.AccessToken) ||
				string.IsNullOrWhiteSpace(state.RefreshToken) ||
				string.IsNullOrWhiteSpace(state.UserId))
			{
				await SafeDeleteAuthFileAsync(filePath);
				return;
			}

			var tokens = new AuthTokens(
				state.AccessToken,
				state.RefreshToken,
				state.UserId,
				state.Email,
				state.ExpiresAtUtc);

			await storage.SaveAsync(filePath, tokens);
		}
		catch
		{
		}
	}

	private static Task SafeDeleteAuthFileAsync(string filePath)
	{
		try
		{
			if (File.Exists(filePath))
				File.Delete(filePath);
		}
		catch
		{
		}

		return Task.CompletedTask;
	}

	private static string? NormalizeAuthValue(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return null;

		var normalized = value.Trim();
		if (string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(normalized, "undefined", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		return normalized;
	}

	private void EnsureInitialized()
	{
		EnsureNotDisposed();
		if (!_initialized)
			throw new InvalidOperationException("Runtime is not initialized. Call InitializeAsync first.");
	}

	private void EnsureNotDisposed()
	{
		if (_disposed)
			throw new ObjectDisposedException(nameof(MeetSpaceMobileRuntime));
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
			return;

		_disposed = true;

		var authStore = Services.GetService<AuthSessionStore>();
		if (authStore != null)
			authStore.StateChanged -= AuthStore_StateChanged;

		if (_initialized)
		{
			try
			{
				await _host.StopAsync();
			}
			catch
			{
			}
		}

		_host.Dispose();
		_initializeLock.Dispose();
	}
}
