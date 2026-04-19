using System;
using MeetSpace.Client.Shared.Stores;

namespace MeetSpace.Client.App.Auth;

public sealed class AuthSessionStore : StoreBase<AuthSessionState>
{
    public AuthSessionStore() : base(AuthSessionState.Empty)
    {
    }

    public void SetSession(AuthTokens tokens)
    {
        var userId = Normalize(tokens.UserId);
        var accessToken = Normalize(tokens.AccessToken);
        var refreshToken = Normalize(tokens.RefreshToken);
        var email = Normalize(tokens.Email);

        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(refreshToken))
        {
            Set(AuthSessionState.Empty);
            return;
        }

        Set(new AuthSessionState(
            true,
            userId,
            email,
            accessToken,
            refreshToken,
            tokens.ExpiresAtUtc));
    }

    public void ClearSession()
    {
        Set(AuthSessionState.Empty);
    }

    private static string? Normalize(string? value)
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
}
