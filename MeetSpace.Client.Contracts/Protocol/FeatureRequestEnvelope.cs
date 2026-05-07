namespace MeetSpace.Client.Contracts.Protocol;
public sealed class FeatureRequestV2
{
    public string Id { get; init; } = string.Empty;
    public string Object { get; init; } = string.Empty;
    public string Agent { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public Dictionary<string, object?> Context { get; init; } = new();
}

public sealed class FeatureRequestEnvelope
{
    public int V { get; init; } = 2;
    public string Kind { get; init; } = "request";
    public string? RequestId { get; init; }
    public Dictionary<string, object?> Meta { get; init; } = new();
    public FeatureRequestV2? Request { get; init; }
    public string Object { get; init; } = string.Empty;
    public string Agent { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public Dictionary<string, object?> Ctx { get; init; } = new();
}