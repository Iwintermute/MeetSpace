
using MeetSpace.Client.App.Conference;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Domain.Calls;
using MeetSpace.Client.Shared.Results;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MeetSpace.Client.App.Calls;

public sealed class CallCoordinator : IDisposable
{
    private readonly IMediasoupFeatureClient _mediasoupClient;
    private readonly IConferenceFeatureClient _conferenceClient;
    private readonly IAudioCallEngine _audioEngine;
    private readonly CallStore _callStore;
    private readonly ConferenceStore _conferenceStore;
    private readonly SessionStore _sessionStore;

    private readonly SemaphoreSlim _sync = new(1, 1);
    private CancellationTokenSource? _backgroundLoopCts;
    private CancellationTokenSource? _joinAttemptCts;


    private string? _conferenceId;
    private string? _roomId;
    private string? _sendTransportId;
    private string? _recvTransportId;
    private string? _localServerProducerId;
    private CancellationTokenSource? _joinCts;
    private CancellationTokenSource? _joinOperationCts;
    private string _joinPhase = "idle";

    private static readonly TimeSpan ServerPhaseTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan BridgePhaseTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan RollbackTimeout = TimeSpan.FromSeconds(5);
    private readonly HashSet<string> _consumedPeers = new(StringComparer.Ordinal);

    public CallCoordinator(
        IMediasoupFeatureClient mediasoupClient,
        IConferenceFeatureClient conferenceClient,
        IAudioCallEngine audioEngine,
        CallStore callStore,
        ConferenceStore conferenceStore,
        SessionStore sessionStore)
    {
        _mediasoupClient = mediasoupClient;
        _conferenceClient = conferenceClient;
        _audioEngine = audioEngine;
        _callStore = callStore;
        _conferenceStore = conferenceStore;
        _sessionStore = sessionStore;

        _conferenceStore.StateChanged += ConferenceStore_StateChanged;
        _audioEngine.TransportConnectRequired += AudioEngine_TransportConnectRequired;
        _audioEngine.TransportProduceRequired += AudioEngine_TransportProduceRequired;
    }

    public Task AttachHostAsync(IAudioBridgeHost host, CancellationToken cancellationToken = default)
    {
        return _audioEngine.AttachAsync(host, cancellationToken);
    }

    public async Task<Result> JoinAudioAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);

        CancellationTokenSource? attemptCts = null;

        try
        {
            if (string.IsNullOrWhiteSpace(conferenceId))
                return Result.Failure(new Error("call.invalid_conference", "Conference ID is empty."));

            if (_callStore.Current.Stage == CallConnectionStage.Connected &&
                string.Equals(_conferenceId, conferenceId, StringComparison.Ordinal))
            {
                return Result.Success();
            }

            if (_callStore.Current.Stage == CallConnectionStage.JoiningRoom ||
                _callStore.Current.Stage == CallConnectionStage.TransportOpening ||
                _callStore.Current.Stage == CallConnectionStage.Negotiating ||
                _callStore.Current.Stage == CallConnectionStage.Publishing)
            {
                try
                {
                    _joinAttemptCts?.Cancel();
                }
                catch
                {
                }

                try
                {
                    if (!string.IsNullOrWhiteSpace(_roomId))
                        await _mediasoupClient.CloseAsync(_roomId, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    await _audioEngine.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }

                _conferenceId = null;
                _roomId = null;
                _sendTransportId = null;
                _recvTransportId = null;
                _localServerProducerId = null;
                _consumedPeers.Clear();
                _callStore.Reset();
            }

            var trustedPeer = _sessionStore.Current.TrustedPeer;
            if (string.IsNullOrWhiteSpace(trustedPeer))
            {
                _callStore.SetStage(CallConnectionStage.Faulted);
                return Result.Failure(new Error("call.no_peer", "Trusted peer is not assigned yet."));
            }

            attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _joinAttemptCts?.Cancel();
            _joinAttemptCts?.Dispose();
            _joinAttemptCts = attemptCts;

            var attemptToken = attemptCts.Token;
            var attemptId = Guid.NewGuid().ToString("N");

            _conferenceId = conferenceId;
            _roomId = "media-" + conferenceId;
            _sendTransportId = "send-" + trustedPeer + "-" + attemptId;
            _recvTransportId = "recv-" + trustedPeer + "-" + attemptId;
            _localServerProducerId = "audio-" + trustedPeer;
            _consumedPeers.Clear();
            _joinPhase = "starting";

            _callStore.SetStage(CallConnectionStage.JoiningRoom, _roomId);

            var createRoom = await RunPhaseAsync(
                "create_room",
                ServerPhaseTimeout,
                ct => _mediasoupClient.CreateRoomIfMissingAsync(_roomId!, ct),
                attemptToken).ConfigureAwait(false);

            if (createRoom.IsFailure)
            {
                _callStore.SetStage(CallConnectionStage.Faulted, _roomId);
                return createRoom;
            }

            var joinRoom = await RunPhaseAsync(
                "join_room",
                ServerPhaseTimeout,
                ct => _mediasoupClient.JoinRoomAsync(_roomId!, ct),
                attemptToken).ConfigureAwait(false);

            if (joinRoom.IsFailure)
            {
                _callStore.SetStage(CallConnectionStage.Faulted, _roomId);
                return joinRoom;
            }

            _callStore.SetStage(CallConnectionStage.TransportOpening, _roomId, _sendTransportId);

            var sendTransport = await RunPhaseAsync(
                "open_send_transport",
                ServerPhaseTimeout,
                ct => _mediasoupClient.OpenTransportAsync(_roomId!, _sendTransportId!, ct),
                attemptToken).ConfigureAwait(false);

            if (sendTransport.IsFailure)
            {
                _callStore.SetStage(CallConnectionStage.Faulted, _roomId, _sendTransportId);
                return Result.Failure(sendTransport.Error!);
            }

            var recvTransport = await RunPhaseAsync(
                "open_recv_transport",
                ServerPhaseTimeout,
                ct => _mediasoupClient.OpenTransportAsync(_roomId!, _recvTransportId!, ct),
                attemptToken).ConfigureAwait(false);

            if (recvTransport.IsFailure)
            {
                _callStore.SetStage(CallConnectionStage.Faulted, _roomId, _recvTransportId);
                return Result.Failure(recvTransport.Error!);
            }

            _callStore.SetStage(CallConnectionStage.Negotiating, _roomId, _sendTransportId);

            await RunPhaseAsync(
                "bridge_load_device",
                BridgePhaseTimeout,
                ct => _audioEngine.LoadDeviceAsync(sendTransport.Value!.RouterRtpCapabilitiesJson, ct),
                attemptToken).ConfigureAwait(false);

            await RunPhaseAsync(
                "bridge_create_send_transport",
                BridgePhaseTimeout,
                ct => _audioEngine.CreateSendTransportAsync(sendTransport.Value!, ct),
                attemptToken).ConfigureAwait(false);

            await RunPhaseAsync(
                "bridge_create_recv_transport",
                BridgePhaseTimeout,
                ct => _audioEngine.CreateRecvTransportAsync(recvTransport.Value!, ct),
                attemptToken).ConfigureAwait(false);

            _callStore.SetStage(CallConnectionStage.Publishing, _roomId, _sendTransportId);

            await RunPhaseAsync(
                "bridge_start_microphone",
                BridgePhaseTimeout,
                ct => _audioEngine.StartMicrophoneAsync(_localServerProducerId!, ct),
                attemptToken).ConfigureAwait(false);

            _callStore.SetMicrophoneEnabled(true);
            _callStore.SetStage(CallConnectionStage.Connected, _roomId, _sendTransportId);

            await _conferenceClient.ListMembersAsync(conferenceId, attemptToken).ConfigureAwait(false);
            StartBackgroundLoop();

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_roomId))
                    await _mediasoupClient.CloseAsync(_roomId, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await _audioEngine.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            _conferenceId = null;
            _roomId = null;
            _sendTransportId = null;
            _recvTransportId = null;
            _localServerProducerId = null;
            _consumedPeers.Clear();
            _callStore.Reset();

            return Result.Failure(new Error("call.join_canceled", $"Audio join canceled at phase '{_joinPhase}'."));
        }
        catch (Exception ex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_roomId))
                    await _mediasoupClient.CloseAsync(_roomId, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await _audioEngine.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            _consumedPeers.Clear();
            _callStore.SetStage(CallConnectionStage.Faulted, _roomId, _sendTransportId);

            return Result.Failure(new Error("call.join_failed", $"JoinAudio failed at phase '{_joinPhase}': {ex.Message}"));
        }
        finally
        {
            if (ReferenceEquals(_joinAttemptCts, attemptCts))
            {
                _joinAttemptCts = null;
                attemptCts?.Dispose();
            }

            _sync.Release();
        }
    }
    public async Task<Result> ToggleMicrophoneAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var nextState = !_callStore.Current.LocalMedia.MicrophoneEnabled;
            await _audioEngine.SetMicrophoneEnabledAsync(nextState, cancellationToken).ConfigureAwait(false);
            _callStore.SetMicrophoneEnabled(nextState);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _callStore.SetStage(CallConnectionStage.Faulted, _roomId, _sendTransportId);
            return Result.Failure(new Error("call.mic_toggle_failed", ex.Message));
        }
    }
    private void CancelJoinOperation()
    {
        var cts = _joinOperationCts;
        _joinOperationCts = null;

        if (cts == null)
            return;

        try
        {
            cts.Cancel();
        }
        catch
        {
        }

        try
        {
            cts.Dispose();
        }
        catch
        {
        }
    }

    private async Task RunPhaseAsync(
        string phase,
        TimeSpan timeout,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        _joinPhase = phase;

        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        phaseCts.CancelAfter(timeout);

        try
        {
            await action(phaseCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"JoinAudio phase '{phase}' timed out after {timeout.TotalSeconds:0} seconds.");
        }
    }

    private async Task<T> RunPhaseAsync<T>(
        string phase,
        TimeSpan timeout,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        _joinPhase = phase;

        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        phaseCts.CancelAfter(timeout);

        try
        {
            return await action(phaseCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"JoinAudio phase '{phase}' timed out after {timeout.TotalSeconds:0} seconds.");
        }
    }
    public async Task<Result> LeaveAudioAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _joinAttemptCts?.Cancel();
        }
        catch
        {
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _backgroundLoopCts?.Cancel();
            _backgroundLoopCts?.Dispose();
            _backgroundLoopCts = null;

            if (!string.IsNullOrWhiteSpace(_roomId))
                await _mediasoupClient.CloseAsync(_roomId, CancellationToken.None).ConfigureAwait(false);

            await _audioEngine.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            _conferenceId = null;
            _roomId = null;
            _sendTransportId = null;
            _recvTransportId = null;
            _localServerProducerId = null;
            _consumedPeers.Clear();

            _callStore.Reset();
            return Result.Success();
        }
        catch (Exception ex)
        {
            _callStore.SetStage(CallConnectionStage.Faulted, _roomId, _sendTransportId);
            return Result.Failure(new Error("call.leave_failed", ex.Message));
        }
        finally
        {
            _sync.Release();
        }
    }
    private void StartBackgroundLoop()
    {
        _backgroundLoopCts?.Cancel();
        _backgroundLoopCts?.Dispose();
        _backgroundLoopCts = new CancellationTokenSource();

        var token = _backgroundLoopCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(_conferenceId))
                        await _conferenceClient.ListMembersAsync(_conferenceId, token).ConfigureAwait(false);

                    await SyncRemoteConsumersAsync(token).ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }, token);
    }

    private async Task AudioEngine_TransportConnectRequired(TransportConnectRequest request)
    {
        if (string.IsNullOrWhiteSpace(_roomId))
        {
            await _audioEngine.ResolveTransportConnectAsync(
                request.PendingId,
                false,
                "Room is not initialized.").ConfigureAwait(false);
            return;
        }

        try
        {
            var token = _joinAttemptCts?.Token ?? CancellationToken.None;

            var result = await _mediasoupClient.ConnectTransportAsync(
                _roomId,
                request.TransportId,
                request.DtlsParametersJson,
                token).ConfigureAwait(false);

            await _audioEngine.ResolveTransportConnectAsync(
                request.PendingId,
                result.IsSuccess,
                result.Error?.Message).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await _audioEngine.ResolveTransportConnectAsync(
                request.PendingId,
                false,
                "Join canceled.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _audioEngine.ResolveTransportConnectAsync(
                request.PendingId,
                false,
                ex.Message).ConfigureAwait(false);
        }
    }

    private async Task AudioEngine_TransportProduceRequired(TransportProduceRequest request)
    {
        if (string.IsNullOrWhiteSpace(_roomId))
        {
            await _audioEngine.ResolveProduceAsync(
                request.PendingId,
                request.ServerProducerId,
                false,
                "Room is not initialized.").ConfigureAwait(false);
            return;
        }

        try
        {
            var token = _joinAttemptCts?.Token ?? CancellationToken.None;

            var result = await _mediasoupClient.ProduceAsync(
                _roomId,
                request.TransportId,
                request.ServerProducerId,
                request.Kind,
                request.RtpParametersJson,
                token).ConfigureAwait(false);

            await _audioEngine.ResolveProduceAsync(
                request.PendingId,
                request.ServerProducerId,
                result.IsSuccess,
                result.Error?.Message).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await _audioEngine.ResolveProduceAsync(
                request.PendingId,
                request.ServerProducerId,
                false,
                "Join canceled.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _audioEngine.ResolveProduceAsync(
                request.PendingId,
                request.ServerProducerId,
                false,
                ex.Message).ConfigureAwait(false);
        }
    }

    private async void ConferenceStore_StateChanged(object? sender, ConferenceViewState state)
    {
        if (state.ActiveConference is null)
            return;

        if (string.IsNullOrWhiteSpace(_conferenceId))
            return;

        if (!string.Equals(state.ActiveConference.ConferenceId, _conferenceId, StringComparison.Ordinal))
            return;

        var selfPeer = _sessionStore.Current.TrustedPeer;
        var participants = state.ActiveConference.Members
            .Where(x => !string.Equals(x.PeerId, selfPeer, StringComparison.Ordinal))
            .Select(x => new RemoteParticipantState(
                x.PeerId,
                HasAudio: true,
                HasVideo: false,
                HasScreenShare: false,
                IsSpeaking: false))
            .ToList();

        _callStore.SetParticipants(participants);

        await SyncRemoteConsumersAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task SyncRemoteConsumersAsync(CancellationToken cancellationToken)
    {
        if (_callStore.Current.Stage != CallConnectionStage.Connected)
            return;

        if (string.IsNullOrWhiteSpace(_roomId) ||
            string.IsNullOrWhiteSpace(_recvTransportId) ||
            string.IsNullOrWhiteSpace(_audioEngine.RecvRtpCapabilitiesJson))
            return;

        var activeConference = _conferenceStore.Current.ActiveConference;
        if (activeConference is null)
            return;

        if (!string.Equals(activeConference.ConferenceId, _conferenceId, StringComparison.Ordinal))
            return;

        var selfPeer = _sessionStore.Current.TrustedPeer;

        foreach (var member in activeConference.Members)
        {
            if (string.Equals(member.PeerId, selfPeer, StringComparison.Ordinal))
                continue;

            if (_consumedPeers.Contains(member.PeerId))
                continue;

            var remoteProducerId = "audio-" + member.PeerId;

            var consumeResult = await _mediasoupClient.ConsumeAsync(
                _roomId,
                _recvTransportId,
                remoteProducerId,
                _audioEngine.RecvRtpCapabilitiesJson!,
                cancellationToken).ConfigureAwait(false);

            if (consumeResult.IsFailure)
                continue;

            await _audioEngine.ConsumeRemoteAudioAsync(consumeResult.Value!, cancellationToken).ConfigureAwait(false);
            _consumedPeers.Add(member.PeerId);
        }
    }

    public void Dispose()
    {
        _backgroundLoopCts?.Cancel();
        _backgroundLoopCts?.Dispose();

        _conferenceStore.StateChanged -= ConferenceStore_StateChanged;
        _audioEngine.TransportConnectRequired -= AudioEngine_TransportConnectRequired;
        _audioEngine.TransportProduceRequired -= AudioEngine_TransportProduceRequired;
    }
}