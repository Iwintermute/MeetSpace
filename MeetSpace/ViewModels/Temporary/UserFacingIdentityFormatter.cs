using System;
using System.Linq;

namespace MeetSpace.ViewModels.Temporary;

internal static class UserFacingIdentityFormatter
{
    public static string ResolveParticipantLabel(string? peerId, string? userId, string fallback = "Участник")
    {
        if (!string.IsNullOrWhiteSpace(userId) && !LooksLikeTechnicalId(userId))
            return userId!;

        if (!string.IsNullOrWhiteSpace(userId) && userId!.Contains("@", StringComparison.Ordinal))
            return userId;

        if (!string.IsNullOrWhiteSpace(peerId) && !LooksLikeTechnicalId(peerId))
            return peerId!;

        return fallback;
    }

    public static bool LooksLikeTechnicalId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var normalized = value.Trim();
        if (normalized.Contains("@", StringComparison.Ordinal))
            return false;

        if (normalized.StartsWith("peer_", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("user_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.Length >= 24)
            return true;

        if (normalized.Contains('-', StringComparison.Ordinal) && normalized.Length >= 16)
            return true;

        var digits = normalized.Count(char.IsDigit);
        return digits >= normalized.Length / 2 && normalized.Length >= 10;
    }
}
