using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace MeetSpace.ViewModels.Temporary;

internal static class UserFacingIdentityFormatter
{
    private static readonly Regex GuidLikeRegex = new(
        "^[0-9a-fA-F]{8}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled);

    private static readonly Regex MostlyHexTokenRegex = new(
        "^[0-9a-fA-F\\-_]{20,}$",
        RegexOptions.Compiled);

    public static string ResolveParticipantLabel(string? peerId, string? userId, string fallback = "Участник")
    {
        if (!string.IsNullOrWhiteSpace(userId) && !LooksLikeTechnicalId(userId))
            return userId!;

        if (!string.IsNullOrWhiteSpace(peerId) && !LooksLikeTechnicalId(peerId))
            return peerId!;

        return fallback;
    }

    public static string ResolveUserLabel(
        string? userId,
        string? preferredLabel,
        string? fallbackEmail = null,
        string fallback = "Пользователь")
    {
        if (!string.IsNullOrWhiteSpace(preferredLabel) && !LooksLikeTechnicalId(preferredLabel))
            return preferredLabel!;

        if (!string.IsNullOrWhiteSpace(fallbackEmail) && fallbackEmail!.Contains("@", StringComparison.Ordinal))
            return fallbackEmail!;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            if (userId.Contains("@", StringComparison.Ordinal))
                return userId;

            if (!LooksLikeTechnicalId(userId))
                return userId;
        }

        return fallback;
    }

    public static bool LooksLikeTechnicalId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var normalized = value.Trim();
        if (normalized.Contains("@", StringComparison.Ordinal))
            return false;

        if (normalized.Contains(" ", StringComparison.Ordinal))
            return false;

        if (normalized.StartsWith("peer_", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("user_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (GuidLikeRegex.IsMatch(normalized))
            return true;

        if (MostlyHexTokenRegex.IsMatch(normalized))
            return true;

        var digits = normalized.Count(char.IsDigit);
        return digits >= normalized.Length / 2 && normalized.Length >= 12;
    }
}
