using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace MeetSpace.Client.Media.Models;

public sealed record ScreenSourceInfo(
    string Id,
    string Name, 
    bool IsDisplay, 
    bool IsWindow);