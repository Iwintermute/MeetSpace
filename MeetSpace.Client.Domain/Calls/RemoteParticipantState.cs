using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Domain.Calls;

public sealed record RemoteParticipantState(
    string PeerId,
    bool HasAudio,
    bool HasVideo,
    bool HasScreenShare,
    bool IsSpeaking = false);