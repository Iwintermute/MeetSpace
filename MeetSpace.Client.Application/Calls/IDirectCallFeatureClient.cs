using MeetSpace.Client.Domain.Calls;
using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.App.Calls;

public interface IDirectCallFeatureClient : ICallMediaFeatureClient
{
    Task<Result<DirectCallSessionInfo>> CreateCallAsync(
        string targetUserId,
        string? mode = null,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default);

    Task<Result<DirectCallSessionInfo>> AcceptCallAsync(
        string callId,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default);

    Task<Result<DirectCallSessionInfo>> DeclineCallAsync(
        string callId,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default);

    Task<Result<DirectCallSessionInfo>> HangupCallAsync(
        string callId,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<DirectCallSessionInfo>>> ListActiveCallsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);
}
