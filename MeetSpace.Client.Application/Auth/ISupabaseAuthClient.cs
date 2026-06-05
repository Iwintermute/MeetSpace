namespace MeetSpace.Client.App.Auth;
/// <summary>
/// Defines Supabase authentication operations used by client runtime.
/// </summary>

public interface ISupabaseAuthClient
{
    /// <summary>
    /// Registers a new user account via email/password credentials.
    /// </summary>
    /// <param name="email">User email.</param>
    /// <param name="password">User password.</param>
    /// <param name="cancellationToken">Cancellation token for network request.</param>
    /// <returns>Authentication result including created session or confirmation requirement.</returns>
    Task<AuthResult> SignUpAsync(string email, string password, CancellationToken cancellationToken = default);
    /// <summary>
    /// Authenticates an existing user via email/password credentials.
    /// </summary>
    /// <param name="email">User email.</param>
    /// <param name="password">User password.</param>
    /// <param name="cancellationToken">Cancellation token for network request.</param>
    /// <returns>Authentication result containing tokens on success.</returns>
    Task<AuthResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default);
    /// <summary>
    /// Refreshes an expired or near-expiry auth session by refresh token.
    /// </summary>
    /// <param name="refreshToken">Refresh token issued by Supabase.</param>
    /// <param name="cancellationToken">Cancellation token for network request.</param>
    /// <returns>New token set for continued authenticated session.</returns>
    Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    /// <summary>
    /// Revokes or terminates the current auth session on backend.
    /// </summary>
    /// <param name="accessToken">Current access token to sign out.</param>
    /// <param name="cancellationToken">Cancellation token for network request.</param>
    /// <returns>A task that completes when sign out request is processed.</returns>
    Task SignOutAsync(string accessToken, CancellationToken cancellationToken = default);
}