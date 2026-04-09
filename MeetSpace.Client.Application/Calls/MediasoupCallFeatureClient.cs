using System.Text.Json;
using MeetSpace.Client.Contracts.Calls;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Realtime.Abstractions;
using MeetSpace.Client.Shared.Results;
using MeetSpace.Client.Shared.Utilities;

namespace MeetSpace.Client.App.Calls;

public sealed class MediasoupFeatureClient : IMediasoupFeatureClient
{
    private readonly IRealtimeGateway _gateway;
    private readonly SemaphoreSlim _requestLock = new(1, 1);

    public MediasoupFeatureClient(IRealtimeGateway gateway)
    {
        _gateway = gateway;
    }

    public async Task<Result> CreateRoomIfMissingAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            MediasoupProtocol.Actions.CreateRoom,
            new Dictionary<string, object?>
            {
                ["roomId"] = Guard.NotNullOrWhiteSpace(roomId, nameof(roomId))
            },
            cancellationToken).ConfigureAwait(false);

        if (response.IsSuccess)
            return Result.Success();

        return response.Error != null &&
       response.Error.Message != null &&
       response.Error.Message.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0
    ? Result.Success()
    : Result.Failure(response.Error!);
    }

    public async Task<Result> JoinRoomAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            MediasoupProtocol.Actions.JoinRoom,
            new Dictionary<string, object?>
            {
                ["roomId"] = Guard.NotNullOrWhiteSpace(roomId, nameof(roomId))
            },
            cancellationToken).ConfigureAwait(false);

        return response.IsSuccess
            ? Result.Success()
            : Result.Failure(response.Error!);
    }

    public async Task<Result<WebRtcTransportInfo>> OpenTransportAsync(string roomId, string transportId, CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            MediasoupProtocol.Actions.OpenTransport,
            new Dictionary<string, object?>
            {
                ["roomId"] = Guard.NotNullOrWhiteSpace(roomId, nameof(roomId)),
                ["transportId"] = Guard.NotNullOrWhiteSpace(transportId, nameof(transportId))
            },
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<WebRtcTransportInfo>.Failure(response.Error!);

        try
        {
            var (dataJson, backendJson) = ExtractPayloadJson(response.Value!);

            using (var dataDoc = JsonDocument.Parse(dataJson))
            using (var backendDoc = JsonDocument.Parse(backendJson))
            {
                var data = dataDoc.RootElement;
                var backend = backendDoc.RootElement;

                return Result<WebRtcTransportInfo>.Success(
                    new WebRtcTransportInfo(
                        data.GetProperty("roomId").GetString() ?? roomId,
                        data.GetProperty("transportId").GetString() ?? transportId,
                        data.GetProperty("iceParameters").GetRawText(),
                        data.GetProperty("iceCandidates").GetRawText(),
                        data.GetProperty("dtlsParameters").GetRawText(),
                        backend.GetProperty("routerRtpCapabilities").GetRawText()));
            }
        }
        catch (Exception ex)
        {
            return Result<WebRtcTransportInfo>.Failure(
                new Error("mediasoup.open_transport.parse_failed", ex.Message));
        }
    }

    public async Task<Result> ConnectTransportAsync(string roomId, string transportId, string dtlsParametersJson, CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            MediasoupProtocol.Actions.WebRtcOffer,
            new Dictionary<string, object?>
            {
                ["roomId"] = Guard.NotNullOrWhiteSpace(roomId, nameof(roomId)),
                ["transportId"] = Guard.NotNullOrWhiteSpace(transportId, nameof(transportId)),
                ["dtlsParameters"] = JsonSerializer.Deserialize<JsonElement>(dtlsParametersJson)
            },
            cancellationToken).ConfigureAwait(false);

        return response.IsSuccess
            ? Result.Success()
            : Result.Failure(response.Error!);
    }

    public async Task<Result> ProduceAsync(string roomId, string transportId, string producerId, string kind, string rtpParametersJson, CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            MediasoupProtocol.Actions.Produce,
            new Dictionary<string, object?>
            {
                ["roomId"] = Guard.NotNullOrWhiteSpace(roomId, nameof(roomId)),
                ["transportId"] = Guard.NotNullOrWhiteSpace(transportId, nameof(transportId)),
                ["producerId"] = Guard.NotNullOrWhiteSpace(producerId, nameof(producerId)),
                ["kind"] = Guard.NotNullOrWhiteSpace(kind, nameof(kind)),
                ["rtpParameters"] = JsonSerializer.Deserialize<JsonElement>(rtpParametersJson)
            },
            cancellationToken).ConfigureAwait(false);

        return response.IsSuccess
            ? Result.Success()
            : Result.Failure(response.Error!);
    }

    public async Task<Result<ConsumerInfo>> ConsumeAsync(string roomId, string transportId, string producerId, string recvRtpCapabilitiesJson, CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            MediasoupProtocol.Actions.Consume,
            new Dictionary<string, object?>
            {
                ["roomId"] = Guard.NotNullOrWhiteSpace(roomId, nameof(roomId)),
                ["transportId"] = Guard.NotNullOrWhiteSpace(transportId, nameof(transportId)),
                ["producerId"] = Guard.NotNullOrWhiteSpace(producerId, nameof(producerId)),
                ["rtpCapabilities"] = JsonSerializer.Deserialize<JsonElement>(recvRtpCapabilitiesJson)
            },
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<ConsumerInfo>.Failure(response.Error!);

        try
        {
            var (dataJson, _) = ExtractPayloadJson(response.Value!);

            using (var dataDoc = JsonDocument.Parse(dataJson))
            {
                var data = dataDoc.RootElement;

                return Result<ConsumerInfo>.Success(
                    new ConsumerInfo(
                        data.GetProperty("consumerId").GetString() ?? string.Empty,
                        data.GetProperty("producerId").GetString() ?? producerId,
                        data.GetProperty("kind").GetString() ?? "audio",
                        data.GetProperty("rtpParameters").GetRawText()));
            }
        }
        catch (Exception ex)
        {
            return Result<ConsumerInfo>.Failure(
                new Error("mediasoup.consume.parse_failed", ex.Message));
        }
    }

    public async Task<Result> CloseAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            MediasoupProtocol.Actions.WebRtcClose,
            new Dictionary<string, object?>
            {
                ["roomId"] = Guard.NotNullOrWhiteSpace(roomId, nameof(roomId))
            },
            cancellationToken).ConfigureAwait(false);

        return response.IsSuccess
            ? Result.Success()
            : Result.Failure(response.Error!);
    }

    private async Task<Result<FeatureResponseEnvelope>> DispatchAsync(string action, Dictionary<string, object?> ctx, CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var requestId = Guid.NewGuid().ToString("N");
            ctx["clientRequestId"] = requestId;
            ctx["correlationId"] = requestId;

            var tcs = new TaskCompletionSource<FeatureResponseEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object? sender, FeatureResponseEnvelope envelope)
            {
                if (!string.Equals(envelope.Type, ProtocolMessageTypes.DispatchResult, StringComparison.Ordinal))
                    return;

                if (!string.Equals(envelope.Object, MediasoupProtocol.Object, StringComparison.Ordinal))
                    return;

                if (!string.Equals(envelope.Action, action, StringComparison.Ordinal))
                    return;

                var responseId = envelope.GetString("clientRequestId") ?? envelope.GetString("correlationId");
                if (!string.IsNullOrWhiteSpace(responseId) &&
                    !string.Equals(responseId, requestId, StringComparison.Ordinal))
                    return;

                tcs.TrySetResult(envelope);
            }

            _gateway.EnvelopeReceived += Handler;
            try
            {
                await _gateway.SendAsync(new FeatureRequestEnvelope
                {
                    Object = MediasoupProtocol.Object,
                    Agent = MediasoupProtocol.DefaultAgent,
                    Action = action,
                    Ctx = ctx
                }, cancellationToken).ConfigureAwait(false);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
                using var _ = timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token));

                var envelope = await tcs.Task.ConfigureAwait(false);
                if (envelope.Ok == false)
                {
                    return Result<FeatureResponseEnvelope>.Failure(
                        new Error("mediasoup.dispatch_failed", envelope.Message ?? $"{action} failed."));
                }

                return Result<FeatureResponseEnvelope>.Success(envelope);
            }
            catch (OperationCanceledException)
            {
                return Result<FeatureResponseEnvelope>.Failure(
                    new Error("mediasoup.timeout", $"Timed out while waiting for mediasoup action '{action}'."));
            }
            finally
            {
                _gateway.EnvelopeReceived -= Handler;
            }
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private static (string dataJson, string backendJson) ExtractPayloadJson(FeatureResponseEnvelope envelope)
    {
        if (envelope.Extensions != null &&
            envelope.Extensions.TryGetValue("data", out var dataElement) &&
            envelope.Extensions.TryGetValue("backend", out var backendElement))
        {
            return (dataElement.GetRawText(), backendElement.GetRawText());
        }

        if (!string.IsNullOrWhiteSpace(envelope.Message))
        {
            using (var doc = JsonDocument.Parse(envelope.Message))
            {
                var root = doc.RootElement;

                if (root.TryGetProperty("data", out var fallbackData) &&
                    root.TryGetProperty("backend", out var fallbackBackend))
                {
                    return (fallbackData.GetRawText(), fallbackBackend.GetRawText());
                }
            }
        }

        throw new InvalidOperationException("Mediasoup dispatch_result has no data/backend payload.");
    }
}