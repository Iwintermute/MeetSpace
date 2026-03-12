using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Domain.Session;

public enum ConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Faulted = 4
}