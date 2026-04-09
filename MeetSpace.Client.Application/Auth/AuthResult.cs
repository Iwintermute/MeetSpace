namespace MeetSpace.Client.App.Auth;

public sealed record AuthResult(
    bool IsAuthenticated,
    bool RequiresEmailConfirmation,
    AuthTokens? Tokens,
    string? Message);