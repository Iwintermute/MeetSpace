using System;

namespace MeetSpace.Client.App.Auth;

public sealed record AuthTokens(
    string AccessToken,
    string RefreshToken,
    string UserId,
    string? Email,
    DateTimeOffset? ExpiresAtUtc);