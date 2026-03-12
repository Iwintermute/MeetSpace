using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Domain.Conference;

public sealed record ConferenceSummary(
    string ConferenceId,
    string Title,
    int MemberCount,
    bool IsClosed = false);