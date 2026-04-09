using System;

namespace MeetSpace.Client.App.Auth;

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