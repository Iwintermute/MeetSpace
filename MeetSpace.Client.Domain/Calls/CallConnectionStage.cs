using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Domain.Calls;

public enum CallConnectionStage
{
    Idle = 0,
    JoiningRoom = 1,
    TransportOpening = 2,
    Publishing = 3,
    Negotiating = 4,
    Connected = 5,
    Faulted = 6
}