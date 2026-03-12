using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Text.Json;
using MeetSpace.Client.Contracts.Protocol;

namespace MeetSpace.Client.Realtime.Serialization;

public sealed class ProtocolJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public string SerializeRequest(FeatureRequestEnvelope request)
        => JsonSerializer.Serialize(request, Options);

    public FeatureResponseEnvelope? DeserializeResponse(string payload)
        => JsonSerializer.Deserialize<FeatureResponseEnvelope>(payload, Options);
}