using MeetSpace.Client.Domain.Calls;


namespace MeetSpace.Client.App.Calls;

public sealed record CallViewState(
    bool IsBusy,
    CallSessionState Session,
    string? SendTransportId,
    string? ReceiveTransportId,
    string? LocalProducerId,
    string? LastError)
{
    public static CallViewState Empty { get; } = new(
        false,
        CallSessionState.Empty,
        null,
        null,
        null,
        null);
}