namespace MeetSpace.Client.App.Auth;

public interface ISupabaseAuthClient
{
    Task<AuthResult> SignUpAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<AuthResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task SignOutAsync(string accessToken, CancellationToken cancellationToken = default);
}