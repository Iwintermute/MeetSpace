using MeetSpace.Client.App.Conference;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Domain.Calls;
using MeetSpace.Client.Domain.Conference;
using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.App.Calls;

public sealed class CallCoordinator : IDisposable
{
    private static readonly TimeSpan ServerPhaseTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan BridgePhaseTimeout = TimeSpan.FromSeconds(12);

    private readonly IConferenceMediaFeatureClient _conferenceMediaClient;
    private readonly IDirectCallFeatureClient _directCallClient;
    private readonly IConferenceFeatureClient _conferenceClient;
    private readonly IAudioCallEngine _audioEngine;
    private readonly CallStore _callStore;
    private readonly ConferenceStore _conferenceStore;
    private readonly SessionStore _sessionStore;

    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly SemaphoreSlim _remoteSync = new(1, 1);
    private readonly HashSet<string> _localProducerIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _localProducerByTrackType = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _consumedProducerIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _consumerIdByProducerId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _consumeInFlightProducerIds = new(StringComparer.Ordinal);

    private CancellationTokenSource? _backgroundLoopCts;
    private CancellationTokenSource? _joinAttemptCts;

    private string? _conversationId;
    private string? _sessionId;
    private string? _roomId;
    private string? _sendTransportId;
    private string? _recvTransportId;
    private string _joinPhase = "idle";
    private CallKind _kind = CallKind.Unknown;

    public CallCoordinator(
        IConferenceMediaFeatureClient conferenceMediaClient,
        IDirectCallFeatureClient directCallClient,
        IConferenceFeatureClient conferenceClient,
        IAudioCallEngine audioEngine,
        CallStore callStore,
        ConferenceStore conferenceStore,
        SessionStore sessionStore)
    {
        _conferenceMediaClient = conferenceMediaClient;
        _directCallClient = directCallClient;
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
        => _audioEngine.AttachAsync(host, cancellationToken);

    public Task<Result> JoinAudioAsync(string conferenceId, CancellationToken cancellationToken = default)
        => JoinSessionAsync(CallKind.Conference, conferenceId, conferenceId, cancellationToken);

    public Task<Result> JoinDirectCallMediaAsync(string callId, CancellationToken cancellationToken = default)
        => JoinSessionAsync(CallKind.Direct, callId, callId, cancellationToken);

    public async Task<Result<string>> StartDirectCallAsync(
        string targetUserId,
        string mode = "audio",
        CancellationToken cancellationToken = default)
    {
        var created = await _directCallClient
            .CreateCallAsync(targetUserId, mode, null, cancellationToken)
            .ConfigureAwait(false);

        if (created.IsFailure)
            return Result<string>.Failure(created.Error!);

        return Result<string>.Success(created.Value!.CallId);
    }

    public async Task<Result> AcceptAndJoinDirectCallAsync(string callId, CancellationToken cancellationToken = default)
    {
        var acceptResult = await _directCallClient.AcceptCallAsync(callId, null, cancellationToken).ConfigureAwait(false);
        if (acceptResult.IsFailure)
            return Result.Failure(acceptResult.Error!);

        return await JoinDirectCallMediaAsync(callId, cancellationToken).ConfigureAwait(false);
    }

    public Task<Result> DeclineDirectCallAsync(
        string callId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        return DeclineDirectCallCoreAsync(callId, reason, cancellationToken);
    }

    public async Task<Result> EndDirectCallAsync(
        string callId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var endResult = await _directCallClient.HangupCallAsync(callId, reason, cancellationToken).ConfigureAwait(false);
        if (endResult.IsFailure)
            return Result.Failure(endResult.Error!);

        if (_kind == CallKind.Direct && string.Equals(_sessionId, callId, StringComparison.Ordinal))
            return await LeaveAudioAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    private async Task<Result> DeclineDirectCallCoreAsync(
        string callId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var result = await _directCallClient.DeclineCallAsync(callId, reason, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error!);
    }

    public async Task<Result> ToggleMicrophoneAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_callStore.Current.Stage != CallConnectionStage.Connected)
                return Result.Failure(new Error("call.not_connected", "Call is not connected."));

            var nextState = !_callStore.Current.LocalMedia.MicrophoneEnabled;
            if (_localProducerByTrackType.TryGetValue("microphone", out var producerId) &&
                !string.IsNullOrWhiteSpace(_sessionId))
            {
                var trackControlResult = nextState
                    ? await ResumeTrackAsync(_kind, _sessionId!, producerId, cancellationToken).ConfigureAwait(false)
                    : await PauseTrackAsync(_kind, _sessionId!, producerId, cancellationToken).ConfigureAwait(false);

                if (trackControlResult.IsFailure)
                    return Result.Failure(trackControlResult.Error!);
            }
            await _audioEngine.SetMicrophoneEnabledAsync(nextState, cancellationToken).ConfigureAwait(false);
            _callStore.SetMicrophoneEnabled(nextState);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _callStore.SetStage(
                CallConnectionStage.Faulted,
                _conversationId,
                _roomId,
                _sendTransportId,
                _sessionId,
                _kind);
            return Result.Failure(new Error("call.mic_toggle_failed", ex.Message));
        }
    }

    public async Task<Result> ToggleCameraAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_callStore.Current.Stage != CallConnectionStage.Connected)
                return Result.Failure(new Error("call.not_connected", "Call is not connected."));

            var nextState = !_callStore.Current.LocalMedia.CameraEnabled;
            if (nextState)
            {
                if (_localProducerByTrackType.TryGetValue("camera", out var existingProducerId))
                {
                    if (!string.IsNullOrWhiteSpace(_sessionId))
                    {
                        var resumeResult = await ResumeTrackAsync(_kind, _sessionId!, existingProducerId, cancellationToken).ConfigureAwait(false);
                        if (resumeResult.IsFailure && !IsProducerMissingError(resumeResult.Error))
                            return Result.Failure(resumeResult.Error!);

                        if (resumeResult.IsFailure)
                        {
                            _localProducerByTrackType.Remove("camera");
                            _localProducerIds.Remove(existingProducerId);
                            return await StartTrackAsync(CallMediaTrackType.Video, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    await _audioEngine.SetCameraEnabledAsync(true, cancellationToken).ConfigureAwait(false);
                    _callStore.SetCameraEnabled(true);
                    return Result.Success();
                }

                return await StartTrackAsync(CallMediaTrackType.Video, cancellationToken).ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(_sessionId) &&
                _localProducerByTrackType.TryGetValue("camera", out var cameraProducerId))
            {
                var pauseResult = await PauseTrackAsync(_kind, _sessionId!, cameraProducerId, cancellationToken).ConfigureAwait(false);
                if (pauseResult.IsFailure)
                    return Result.Failure(pauseResult.Error!);
            }

            await _audioEngine.SetCameraEnabledAsync(false, cancellationToken).ConfigureAwait(false);
            _callStore.SetCameraEnabled(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _callStore.SetStage(
                CallConnectionStage.Faulted,
                _conversationId,
                _roomId,
                _sendTransportId,
                _sessionId,
                _kind);
            return Result.Failure(new Error("call.camera_toggle_failed", ex.Message));
        }
    }

    public async Task<Result> ToggleScreenShareAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_callStore.Current.Stage != CallConnectionStage.Connected)
                return Result.Failure(new Error("call.not_connected", "Call is not connected."));

            var nextState = !_callStore.Current.LocalMedia.ScreenShareEnabled;
            if (nextState)
            {
                if (_localProducerByTrackType.TryGetValue("screen", out var existingProducerId))
                {
                    if (!string.IsNullOrWhiteSpace(_sessionId))
                    {
                        var resumeResult = await ResumeTrackAsync(_kind, _sessionId!, existingProducerId, cancellationToken).ConfigureAwait(false);
                        if (resumeResult.IsFailure && !IsProducerMissingError(resumeResult.Error))
                            return Result.Failure(resumeResult.Error!);

                        if (resumeResult.IsFailure)
                        {
                            _localProducerByTrackType.Remove("screen");
                            _localProducerIds.Remove(existingProducerId);
                            return await StartTrackAsync(CallMediaTrackType.ScreenShare, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    await _audioEngine.SetScreenShareEnabledAsync(true, cancellationToken).ConfigureAwait(false);
                    _callStore.SetScreenShareEnabled(true);
                    return Result.Success();
                }

                return await StartTrackAsync(CallMediaTrackType.ScreenShare, cancellationToken).ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(_sessionId) &&
                _localProducerByTrackType.TryGetValue("screen", out var screenProducerId))
            {
                var closeResult = await CloseTrackAsync(_kind, _sessionId!, screenProducerId, cancellationToken).ConfigureAwait(false);
                if (closeResult.IsFailure)
                    return Result.Failure(closeResult.Error!);
            }

            await _audioEngine.SetScreenShareEnabledAsync(false, cancellationToken).ConfigureAwait(false);
            await _audioEngine.StopScreenShareAsync(cancellationToken).ConfigureAwait(false);

            if (_localProducerByTrackType.TryGetValue("screen", out var producerId))
            {
                _localProducerByTrackType.Remove("screen");
                _localProducerIds.Remove(producerId);
            }

            _callStore.SetScreenShareEnabled(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _callStore.SetStage(
                CallConnectionStage.Faulted,
                _conversationId,
                _roomId,
                _sendTransportId,
                _sessionId,
                _kind);
            return Result.Failure(new Error("call.screen_toggle_failed", ex.Message));
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
            await ResetCurrentCallAsync().ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _callStore.SetStage(
                CallConnectionStage.Faulted,
                _conversationId,
                _roomId,
                _sendTransportId,
                _sessionId,
                _kind);
            return Result.Failure(new Error("call.leave_failed", ex.Message));
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task<Result> JoinSessionAsync(
        CallKind kind,
        string sessionId,
        string conversationId,
        CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? attemptCts = null;

        try
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return Result.Failure(new Error("call.invalid_session", "Session ID is empty."));

            if (_callStore.Current.Stage == CallConnectionStage.Connected &&
                _kind == kind &&
                string.Equals(_sessionId, sessionId, StringComparison.Ordinal))
            {
                return Result.Success();
            }

            await ResetCurrentCallAsync().ConfigureAwait(false);

            var selfPeerId = _sessionStore.Current.SelfPeerId;
            if (string.IsNullOrWhiteSpace(selfPeerId))
            {
                _callStore.SetStage(
                    CallConnectionStage.Faulted,
                    conversationId,
                    sessionId,
                    null,
                    sessionId,
                    kind);
                return Result.Failure(new Error("call.self_peer_missing", "Self peer is not assigned yet."));
            }

            attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _joinAttemptCts?.Cancel();
            _joinAttemptCts?.Dispose();
            _joinAttemptCts = attemptCts;

            var token = attemptCts.Token;
            var attemptId = Guid.NewGuid().ToString("N");

            _kind = kind;
            _sessionId = sessionId;
            _conversationId = conversationId;
            _roomId = sessionId;
            _sendTransportId = $"send-{selfPeerId}-{attemptId}";
            _recvTransportId = $"recv-{selfPeerId}-{attemptId}";
            _joinPhase = "starting";

            _localProducerIds.Clear();
            _localProducerByTrackType.Clear();
            _consumedProducerIds.Clear();
            _consumerIdByProducerId.Clear();
            _consumeInFlightProducerIds.Clear();

            _callStore.SetStage(
                CallConnectionStage.JoiningRoom,
                _conversationId,
                _roomId,
                _sendTransportId,
                _sessionId,
                _kind);

            var sendTransport = await RunPhaseAsync(
                "open_send_transport",
                ServerPhaseTimeout,
                ct => OpenTransportAsync(kind, sessionId, _sendTransportId!, ct),
                token).ConfigureAwait(false);

            if (sendTransport.IsFailure)
            {
                _callStore.SetStage(
                    CallConnectionStage.Faulted,
                    _conversationId,
                    _roomId,
                    _sendTransportId,
                    _sessionId,
                    _kind);
                return Result.Failure(sendTransport.Error!);
            }

            var recvTransport = await RunPhaseAsync(
                "open_recv_transport",
                ServerPhaseTimeout,
                ct => OpenTransportAsync(kind, sessionId, _recvTransportId!, ct),
                token).ConfigureAwait(false);

            if (recvTransport.IsFailure)
            {
                _callStore.SetStage(
                    CallConnectionStage.Faulted,
                    _conversationId,
                    _roomId,
                    _recvTransportId,
                    _sessionId,
                    _kind);
                return Result.Failure(recvTransport.Error!);
            }

            _roomId = sendTransport.Value!.RoomId;

            _callStore.SetStage(
                CallConnectionStage.Negotiating,
                _conversationId,
                _roomId,
                _sendTransportId,
                _sessionId,
                _kind);

            await RunPhaseAsync(
                "bridge_load_device",
                BridgePhaseTimeout,
                ct => _audioEngine.LoadDeviceAsync(sendTransport.Value!.RouterRtpCapabilitiesJson, ct),
                token).ConfigureAwait(false);

            await RunPhaseAsync(
                "bridge_create_send_transport",
                BridgePhaseTimeout,
                ct => _audioEngine.CreateSendTransportAsync(sendTransport.Value!, ct),
                token).ConfigureAwait(false);

            await RunPhaseAsync(
                "bridge_create_recv_transport",
                BridgePhaseTimeout,
                ct => _audioEngine.CreateRecvTransportAsync(recvTransport.Value!, ct),
                token).ConfigureAwait(false);

            _callStore.SetStage(
                CallConnectionStage.Publishing,
                _conversationId,
                _roomId,
                _sendTransportId,
                _sessionId,
                _kind);

            var startAudioResult = await RunPhaseAsync(
                "bridge_start_microphone",
                BridgePhaseTimeout,
                ct => StartTrackAsync(CallMediaTrackType.Audio, ct),
                token).ConfigureAwait(false);

            if (startAudioResult.IsFailure)
            {
                _callStore.SetStage(
                    CallConnectionStage.Faulted,
                    _conversationId,
                    _roomId,
                    _sendTransportId,
                    _sessionId,
                    _kind);
                return startAudioResult;
            }

            _callStore.SetStage(
                CallConnectionStage.Connected,
                _conversationId,
                _roomId,
                _sendTransportId,
                _sessionId,
                _kind);

            if (_kind == CallKind.Conference && !string.IsNullOrWhiteSpace(_conversationId))
                _ = _conferenceClient.ListMembersAsync(_conversationId, token);

            StartBackgroundLoop();
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            await ResetCurrentCallAsync().ConfigureAwait(false);
            return Result.Failure(new Error("call.join_canceled", $"Join canceled at phase '{_joinPhase}'."));
        }
        catch (Exception ex)
        {
            await ResetCurrentCallAsync().ConfigureAwait(false);
            _callStore.SetStage(
                CallConnectionStage.Faulted,
                _conversationId,
                _roomId,
                _sendTransportId,
                _sessionId,
                _kind);
            return Result.Failure(new Error("call.join_failed", $"Join failed at phase '{_joinPhase}': {ex.Message}"));
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

    private async Task<Result> StartTrackAsync(CallMediaTrackType trackType, CancellationToken cancellationToken)
    {
        var selfPeerId = _sessionStore.Current.SelfPeerId;
        if (string.IsNullOrWhiteSpace(selfPeerId))
            return Result.Failure(new Error("call.self_peer_missing", "Self peer is not assigned yet."));

        var normalizedTrackType = NormalizeTrackType(trackType);
        if (_localProducerByTrackType.TryGetValue(normalizedTrackType, out _))
        {
            switch (trackType)
            {
                case CallMediaTrackType.Audio:
                    await _audioEngine.SetMicrophoneEnabledAsync(true, cancellationToken).ConfigureAwait(false);
                    _callStore.SetMicrophoneEnabled(true);
                    return Result.Success();
                case CallMediaTrackType.Video:
                    await _audioEngine.SetCameraEnabledAsync(true, cancellationToken).ConfigureAwait(false);
                    _callStore.SetCameraEnabled(true);
                    return Result.Success();
                case CallMediaTrackType.ScreenShare:
                    await _audioEngine.SetScreenShareEnabledAsync(true, cancellationToken).ConfigureAwait(false);
                    _callStore.SetScreenShareEnabled(true);
                    return Result.Success();
            }
        }

        var producerPrefix = normalizedTrackType switch
        {
            "microphone" => "audio",
            "camera" => "video",
            "screen" => "screen",
            _ => normalizedTrackType
        };
        var producerId = $"{producerPrefix}-{selfPeerId}-{Guid.NewGuid():N}";

        _localProducerIds.Add(producerId);
        _localProducerByTrackType[normalizedTrackType] = producerId;

        try
        {
            switch (trackType)
            {
                case CallMediaTrackType.Audio:
                    await _audioEngine.StartMicrophoneAsync(producerId, cancellationToken).ConfigureAwait(false);
                    _callStore.SetMicrophoneEnabled(true);
                    break;
                case CallMediaTrackType.Video:
                    await _audioEngine.StartCameraAsync(producerId, cancellationToken).ConfigureAwait(false);
                    _callStore.SetCameraEnabled(true);
                    break;
                case CallMediaTrackType.ScreenShare:
                    await _audioEngine.StartScreenShareAsync(producerId, cancellationToken).ConfigureAwait(false);
                    _callStore.SetScreenShareEnabled(true);
                    break;
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            _localProducerIds.Remove(producerId);
            _localProducerByTrackType.Remove(normalizedTrackType);
            return Result.Failure(new Error("call.start_track_failed", ex.Message));
        }
    }

    private async Task ResetCurrentCallAsync()
    {
        _backgroundLoopCts?.Cancel();
        _backgroundLoopCts?.Dispose();
        _backgroundLoopCts = null;

        var previousKind = _kind;
        var previousSessionId = _sessionId;

        try
        {
            if (!string.IsNullOrWhiteSpace(previousSessionId))
            {
                if (previousKind == CallKind.Direct)
                    await _directCallClient.CloseSessionAsync(previousSessionId, CancellationToken.None).ConfigureAwait(false);
                else if (previousKind == CallKind.Conference)
                    await _conferenceMediaClient.CloseSessionAsync(previousSessionId, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
        }

        try
        {
            if (previousKind == CallKind.Direct && !string.IsNullOrWhiteSpace(previousSessionId))
                await _directCallClient.HangupCallAsync(previousSessionId, "client_leave", CancellationToken.None).ConfigureAwait(false);
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

        _conversationId = null;
        _sessionId = null;
        _roomId = null;
        _sendTransportId = null;
        _recvTransportId = null;
        _kind = CallKind.Unknown;
        _joinPhase = "idle";

        _localProducerIds.Clear();
        _localProducerByTrackType.Clear();
        _consumedProducerIds.Clear();
        _consumerIdByProducerId.Clear();
        _consumeInFlightProducerIds.Clear();

        _callStore.Reset();
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
            throw new TimeoutException($"Join phase '{phase}' timed out after {timeout.TotalSeconds:0} seconds.");
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
            throw new TimeoutException($"Join phase '{phase}' timed out after {timeout.TotalSeconds:0} seconds.");
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
                    if (_kind == CallKind.Conference && !string.IsNullOrWhiteSpace(_conversationId))
                        _ = _conferenceClient.ListMembersAsync(_conversationId, token);

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
        if (string.IsNullOrWhiteSpace(_sessionId))
        {
            await _audioEngine.ResolveTransportConnectAsync(
                request.PendingId,
                false,
                "Call session is not initialized.").ConfigureAwait(false);
            return;
        }

        try
        {
            var token = _joinAttemptCts?.Token ?? CancellationToken.None;

            var result = await ConnectTransportAsync(
                _kind,
                _sessionId!,
                request.TransportId,
                request.DtlsParametersJson,
                token).ConfigureAwait(false);

            await _audioEngine.ResolveTransportConnectAsync(
                request.PendingId,
                result.IsSuccess,
                result.Error?.Message).ConfigureAwait(false);
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
        if (string.IsNullOrWhiteSpace(_sessionId))
        {
            await _audioEngine.ResolveProduceAsync(
                request.PendingId,
                request.ServerProducerId,
                false,
                "Call session is not initialized.").ConfigureAwait(false);
            return;
        }

        var trackType = NormalizeTrackType(request.TrackType, request.Kind);

        try
        {
            var token = _joinAttemptCts?.Token ?? CancellationToken.None;

            var result = await PublishTrackAsync(
                _kind,
                _sessionId!,
                request.TransportId,
                request.ServerProducerId,
                request.Kind,
                trackType,
                request.RtpParametersJson,
                token).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                _localProducerIds.Add(request.ServerProducerId);
                _localProducerByTrackType[trackType] = request.ServerProducerId;
            }

            await _audioEngine.ResolveProduceAsync(
                request.PendingId,
                request.ServerProducerId,
                result.IsSuccess,
                result.Error?.Message).ConfigureAwait(false);
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
        if (_kind != CallKind.Conference || string.IsNullOrWhiteSpace(_conversationId))
            return;

        var active = state.ActiveConference;
        if (active == null)
            return;

        if (!string.Equals(active.ConferenceId, _conversationId, StringComparison.Ordinal))
            return;

        ApplyParticipantsFromConference(active);
        try
        {
            await SyncRemoteConsumersAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void ApplyParticipantsFromConference(ConferenceDetails details)
    {
        var selfPeerId = _sessionStore.Current.SelfPeerId;
        var existing = _callStore.Current.Participants.ToDictionary(x => x.PeerId, x => x, StringComparer.Ordinal);

        var participants = details.Members
            .Where(x => !string.IsNullOrWhiteSpace(x.PeerId))
            .Where(x => !string.Equals(x.PeerId, selfPeerId, StringComparison.Ordinal))
            .Select(member =>
            {
                if (existing.TryGetValue(member.PeerId, out var current))
                    return current with { UserId = ResolveMemberUserLabel(member, current.UserId) };

                return new RemoteParticipantState(
                    member.PeerId,
                    HasAudio: false,
                    HasVideo: false,
                    HasScreenShare: false,
                    IsSpeaking: false,
                    UserId: ResolveMemberUserLabel(member, null));
            })
            .OrderBy(x => x.PeerId, StringComparer.Ordinal)
            .ToList();

        _callStore.SetParticipants(participants);
    }

    private async Task SyncRemoteConsumersAsync(CancellationToken cancellationToken)
    {
        if (_callStore.Current.Stage != CallConnectionStage.Connected)
            return;

        if (string.IsNullOrWhiteSpace(_sessionId) ||
            string.IsNullOrWhiteSpace(_recvTransportId) ||
            string.IsNullOrWhiteSpace(_audioEngine.RecvRtpCapabilitiesJson))
        {
            return;
        }

        if (!await _remoteSync.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            var statsResult = await GetMediaStatsAsync(_kind, _sessionId!, cancellationToken).ConfigureAwait(false);
            if (statsResult.IsFailure)
                return;

            var snapshot = statsResult.Value!;
            var producers = snapshot.Producers ?? Array.Empty<RemoteProducerDescriptor>();
            var selfPeerId = _sessionStore.Current.SelfPeerId;

            ApplyParticipantsFromMediaSnapshot(snapshot, producers);

            var activeRemoteProducerIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var producer in producers)
            {
                if (string.IsNullOrWhiteSpace(producer.ProducerId))
                    continue;

                if (!string.IsNullOrWhiteSpace(selfPeerId) &&
                    string.Equals(producer.PeerId, selfPeerId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (_localProducerIds.Contains(producer.ProducerId))
                    continue;

                activeRemoteProducerIds.Add(producer.ProducerId);

                if (_consumedProducerIds.Contains(producer.ProducerId))
                    continue;

                if (!_consumeInFlightProducerIds.Add(producer.ProducerId))
                    continue;

                try
                {
                    var consumeResult = await ConsumeTrackAsync(
                        _kind,
                        _sessionId!,
                        _recvTransportId!,
                        producer.ProducerId,
                        _audioEngine.RecvRtpCapabilitiesJson!,
                        cancellationToken).ConfigureAwait(false);

                    if (consumeResult.IsFailure)
                        continue;

                    var consumerInfo = consumeResult.Value!;

                    await _audioEngine.ConsumeRemoteTrackAsync(consumerInfo, cancellationToken).ConfigureAwait(false);

                    _consumedProducerIds.Add(producer.ProducerId);
                    _consumerIdByProducerId[producer.ProducerId] = consumerInfo.ConsumerId;

                    _ = ConsumerReadyAsync(_kind, _sessionId!, consumerInfo.ConsumerId, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }
                finally
                {
                    _consumeInFlightProducerIds.Remove(producer.ProducerId);
                }
            }

            var staleProducers = _consumerIdByProducerId.Keys
                .Where(id => !activeRemoteProducerIds.Contains(id))
                .ToList();

            foreach (var producerId in staleProducers)
            {
                if (_consumerIdByProducerId.TryGetValue(producerId, out var consumerId))
                {
                    try
                    {
                        await _audioEngine.RemoveRemoteConsumerAsync(consumerId, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                _consumerIdByProducerId.Remove(producerId);
                _consumedProducerIds.Remove(producerId);
                _consumeInFlightProducerIds.Remove(producerId);
            }
        }
        finally
        {
            _remoteSync.Release();
        }
    }

    private void ApplyParticipantsFromMediaSnapshot(
        MediaStatsSnapshot snapshot,
        IReadOnlyList<RemoteProducerDescriptor> producers)
    {
        var selfPeerId = _sessionStore.Current.SelfPeerId;
        var conference = _conferenceStore.Current.ActiveConference;
        Dictionary<string, string?>? userByPeer = null;

        if (conference != null &&
            !string.IsNullOrWhiteSpace(_conversationId) &&
            string.Equals(conference.ConferenceId, _conversationId, StringComparison.Ordinal))
        {
            userByPeer = conference.Members
                .Where(x => !string.IsNullOrWhiteSpace(x.PeerId))
                .GroupBy(x => x.PeerId, StringComparer.Ordinal)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(member => ResolveMemberUserLabel(member, null)).FirstOrDefault(static user => !string.IsNullOrWhiteSpace(user)),
                    StringComparer.Ordinal);
        }

        var participants = new Dictionary<string, RemoteParticipantState>(StringComparer.Ordinal);

        foreach (var memberPeerId in snapshot.MemberPeerIds ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(memberPeerId) ||
                string.Equals(memberPeerId, selfPeerId, StringComparison.Ordinal))
            {
                continue;
            }

            participants[memberPeerId] = new RemoteParticipantState(
                memberPeerId,
                HasAudio: false,
                HasVideo: false,
                HasScreenShare: false,
                IsSpeaking: false,
                UserId: userByPeer != null && userByPeer.TryGetValue(memberPeerId, out var userId) ? userId : null);
        }

        foreach (var producer in producers)
        {
            if (string.IsNullOrWhiteSpace(producer.PeerId) ||
                string.Equals(producer.PeerId, selfPeerId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!participants.TryGetValue(producer.PeerId, out var participant))
            {
                participants[producer.PeerId] = new RemoteParticipantState(
                    producer.PeerId,
                    HasAudio: false,
                    HasVideo: false,
                    HasScreenShare: false,
                    IsSpeaking: false,
                    UserId: userByPeer != null && userByPeer.TryGetValue(producer.PeerId, out var producerUserId) ? producerUserId : null);

                participant = participants[producer.PeerId];
            }

            var normalizedTrackType = NormalizeTrackType(producer.TrackType, producer.Kind);
            participants[producer.PeerId] = participant with
            {
                HasAudio = participant.HasAudio || normalizedTrackType == "microphone",
                HasVideo = participant.HasVideo || normalizedTrackType == "camera",
                HasScreenShare = participant.HasScreenShare || normalizedTrackType == "screen"
            };
        }

        _callStore.SetParticipants(
            participants.Values
                .OrderBy(x => x.PeerId, StringComparer.Ordinal)
                .ToList());
    }

    private Task<Result<WebRtcTransportInfo>> OpenTransportAsync(
        CallKind kind,
        string sessionId,
        string transportId,
        CancellationToken cancellationToken)
    {
        return kind == CallKind.Direct
            ? _directCallClient.OpenTransportAsync(sessionId, transportId, cancellationToken)
            : _conferenceMediaClient.OpenTransportAsync(sessionId, transportId, cancellationToken);
    }

    private Task<Result> ConnectTransportAsync(
        CallKind kind,
        string sessionId,
        string transportId,
        string dtlsParametersJson,
        CancellationToken cancellationToken)
    {
        return kind == CallKind.Direct
            ? _directCallClient.ConnectTransportAsync(sessionId, transportId, dtlsParametersJson, cancellationToken)
            : _conferenceMediaClient.ConnectTransportAsync(sessionId, transportId, dtlsParametersJson, cancellationToken);
    }

    private Task<Result> PublishTrackAsync(
        CallKind kind,
        string sessionId,
        string transportId,
        string producerId,
        string mediaKind,
        string trackType,
        string rtpParametersJson,
        CancellationToken cancellationToken)
    {
        return kind == CallKind.Direct
            ? _directCallClient.PublishTrackAsync(sessionId, transportId, producerId, mediaKind, trackType, rtpParametersJson, cancellationToken)
            : _conferenceMediaClient.PublishTrackAsync(sessionId, transportId, producerId, mediaKind, trackType, rtpParametersJson, cancellationToken);
    }

    private Task<Result> PauseTrackAsync(
        CallKind kind,
        string sessionId,
        string producerId,
        CancellationToken cancellationToken)
    {
        return kind == CallKind.Direct
            ? _directCallClient.PauseTrackAsync(sessionId, producerId, cancellationToken)
            : _conferenceMediaClient.PauseTrackAsync(sessionId, producerId, cancellationToken);
    }

    private Task<Result> ResumeTrackAsync(
        CallKind kind,
        string sessionId,
        string producerId,
        CancellationToken cancellationToken)
    {
        return kind == CallKind.Direct
            ? _directCallClient.ResumeTrackAsync(sessionId, producerId, cancellationToken)
            : _conferenceMediaClient.ResumeTrackAsync(sessionId, producerId, cancellationToken);
    }

    private Task<Result> CloseTrackAsync(
        CallKind kind,
        string sessionId,
        string producerId,
        CancellationToken cancellationToken)
    {
        return kind == CallKind.Direct
            ? _directCallClient.CloseTrackAsync(sessionId, producerId, cancellationToken)
            : _conferenceMediaClient.CloseTrackAsync(sessionId, producerId, cancellationToken);
    }

    private Task<Result<ConsumerInfo>> ConsumeTrackAsync(
        CallKind kind,
        string sessionId,
        string transportId,
        string producerId,
        string recvRtpCapabilitiesJson,
        CancellationToken cancellationToken)
    {
        return kind == CallKind.Direct
            ? _directCallClient.ConsumeTrackAsync(sessionId, transportId, producerId, recvRtpCapabilitiesJson, null, cancellationToken)
            : _conferenceMediaClient.ConsumeTrackAsync(sessionId, transportId, producerId, recvRtpCapabilitiesJson, null, cancellationToken);
    }

    private Task<Result> ConsumerReadyAsync(
        CallKind kind,
        string sessionId,
        string consumerId,
        CancellationToken cancellationToken)
    {
        return kind == CallKind.Direct
            ? _directCallClient.ConsumerReadyAsync(sessionId, consumerId, cancellationToken)
            : _conferenceMediaClient.ConsumerReadyAsync(sessionId, consumerId, cancellationToken);
    }

    private Task<Result<MediaStatsSnapshot>> GetMediaStatsAsync(
        CallKind kind,
        string sessionId,
        CancellationToken cancellationToken)
    {
        return kind == CallKind.Direct
            ? _directCallClient.GetMediaStatsAsync(sessionId, cancellationToken)
            : _conferenceMediaClient.GetMediaStatsAsync(sessionId, cancellationToken);
    }

    private static string NormalizeTrackType(CallMediaTrackType trackType)
    {
        return trackType switch
        {
            CallMediaTrackType.Audio => "microphone",
            CallMediaTrackType.Video => "camera",
            CallMediaTrackType.ScreenShare => "screen",
            _ => "microphone"
        };
    }

    private static string NormalizeTrackType(string? trackType, string? mediaKind)
    {
        if (!string.IsNullOrWhiteSpace(trackType))
        {
            var normalized = trackType.Trim().ToLowerInvariant();
            return normalized switch
            {
                "mic" => "microphone",
                "audio" => "microphone",
                "microphone" => "microphone",
                "video" => "camera",
                "camera" => "camera",
                "screen_share" => "screen",
                "screenshare" => "screen",
                "screen" => "screen",
                _ => normalized
            };
        }

        if (string.Equals(mediaKind, "video", StringComparison.OrdinalIgnoreCase))
            return "camera";

        return "microphone";
    }

    private static bool IsProducerMissingError(Error? error)
    {
        if (error == null)
            return false;
        return ContainsIgnoreCase(error.Code, "producer_not_found") ||
               ContainsIgnoreCase(error.Code, "not_found") ||
               ContainsIgnoreCase(error.Message, "producer not found") ||
               ContainsIgnoreCase(error.Message, "not found");
    }

    private static bool ContainsIgnoreCase(string? value, string fragment)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string? ResolveMemberUserLabel(ConferenceMember member, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(member.DisplayName))
            return member.DisplayName;

        if (!string.IsNullOrWhiteSpace(member.Email))
            return member.Email;

        if (!string.IsNullOrWhiteSpace(member.UserId))
            return member.UserId;

        return fallback;
    }

    public void Dispose()
    {
        _backgroundLoopCts?.Cancel();
        _backgroundLoopCts?.Dispose();

        _conferenceStore.StateChanged -= ConferenceStore_StateChanged;
        _audioEngine.TransportConnectRequired -= AudioEngine_TransportConnectRequired;
        _audioEngine.TransportProduceRequired -= AudioEngine_TransportProduceRequired;
        _sync.Dispose();
        _remoteSync.Dispose();
    }
}
