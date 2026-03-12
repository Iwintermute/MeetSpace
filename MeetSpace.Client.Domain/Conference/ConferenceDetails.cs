using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Domain.Conference;

public sealed record ConferenceDetails(
    string ConferenceId,
    string OwnerPeerId,
    bool IsClosed,
    ulong Revision,
    IReadOnlyList<ConferenceMember> Members);