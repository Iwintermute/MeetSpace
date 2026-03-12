using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Contracts.Protocol;

public sealed class FeatureRequestEnvelope
{
    public string Object { get; init; } = string.Empty;
    public string Agent { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public Dictionary<string, object?> Ctx { get; init; } = new();
}