using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Domain.Conference;

public sealed record ConferenceMember(
    string PeerId,
    string SessionId,
    bool IsOwner);