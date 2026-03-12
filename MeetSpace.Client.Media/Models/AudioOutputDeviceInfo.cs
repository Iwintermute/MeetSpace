using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Media.Models;

public sealed record AudioOutputDeviceInfo(
    string Id, 
    string Name, 
    bool IsDefault);