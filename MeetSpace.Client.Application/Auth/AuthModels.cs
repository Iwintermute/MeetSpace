namespace MeetSpace.Client.App.Auth;

public sealed record AuthTokens(
    string AccessToken,
    string RefreshToken,
    string UserId,
    string? Email,
    DateTimeOffset? ExpiresAtUtc);

public sealed record AuthResult(
    bool IsAuthenticated,
    bool RequiresEmailConfirmation,
    AuthTokens? Tokens,
    string? Message);

public sealed record AuthSessionState(
    bool IsAuthenticated,
    string? UserId,
    string? Email,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAtUtc)
{
    public static AuthSessionState Empty { get; } = new(false, null, null, null, null, null);
}