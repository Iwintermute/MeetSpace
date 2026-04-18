namespace MeetSpace.Client.Contracts.Protocol;

public sealed class FeatureRequestEnvelope
{
    public string Object { get; init; } = string.Empty;
    public string Agent { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public Dictionary<string, object?> Ctx { get; init; } = new();
}