using System;
using System.Collections.Generic;
using System.Text;

namespace MeetSpace.Client.Shared.Utilities;

public static class ConversationKeys
{
    public static string BuildDirectDialogId(string selfPeerId, string peerId)
    {
        if (string.IsNullOrWhiteSpace(selfPeerId))
            throw new ArgumentException("Self peer id is empty.", nameof(selfPeerId));

        if (string.IsNullOrWhiteSpace(peerId))
            throw new ArgumentException("Peer id is empty.", nameof(peerId));

        return string.CompareOrdinal(selfPeerId, peerId) <= 0
            ? $"dm:{selfPeerId}:{peerId}"
            : $"dm:{peerId}:{selfPeerId}";
    }
}