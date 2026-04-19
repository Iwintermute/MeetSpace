using MeetSpace.Client.App.Calls;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MeetSpace.Temporary
{
    public sealed class WebViewAudioCallEngine : IAudioCallEngine
    {
        private static readonly TimeSpan HostReadyTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan ConsumeVideoCommandTimeout = TimeSpan.FromSeconds(45);

        private readonly SemaphoreSlim _attachSync = new SemaphoreSlim(1, 1);
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingResponses =
            new ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>();

        private IAudioBridgeHost? _host;
        private IAudioBridgeHost? _subscribedHost;
        private TaskCompletionSource<bool>? _hostReadyTcs;

        public event Func<TransportConnectRequest, Task>? TransportConnectRequired;
        public event Func<TransportProduceRequest, Task>? TransportProduceRequired;

        public string? RecvRtpCapabilitiesJson { get; private set; }

        public async Task AttachAsync(IAudioBridgeHost host, CancellationToken cancellationToken = default)
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));

            await _attachSync.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_host, host) &&
                    _hostReadyTcs != null &&
                    IsRanToCompletion(_hostReadyTcs.Task))
                {
                    return;
                }

                DetachCurrentHost();

                var readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                _subscribedHost = host;
                _hostReadyTcs = readyTcs;
                _subscribedHost.MessageReceived += Host_MessageReceived;

                try
                {
                    await host.InitializeAsync(cancellationToken).ConfigureAwait(false);

                    using (var readyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    using (readyCts.Token.Register(() =>
                    {
                        readyTcs.TrySetException(
                            new TimeoutException(
                                "Media host did not send host_ready. WebView page loaded but bridge JS did not initialize."));
                    }))
                    {
                        readyCts.CancelAfter(HostReadyTimeout);
                        await readyTcs.Task.ConfigureAwait(false);
                    }

                    _host = host;
                }
                catch
                {
                    if (ReferenceEquals(_subscribedHost, host))
                    {
                        _subscribedHost.MessageReceived -= Host_MessageReceived;
                        _subscribedHost = null;
                    }

                    if (ReferenceEquals(_host, host))
                        _host = null;

                    if (ReferenceEquals(_hostReadyTcs, readyTcs))
                        _hostReadyTcs = null;

                    throw;
                }
            }
            finally
            {
                _attachSync.Release();
            }
        }

        public async Task<DeviceLoadResult> LoadDeviceAsync(string routerRtpCapabilitiesJson, CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["routerRtpCapabilities"] = JsonSerializer.Deserialize<JsonElement>(routerRtpCapabilitiesJson)
            };

            var response = await SendRequestAsync("load_device", payload, cancellationToken).ConfigureAwait(false);

            string rtpCapabilitiesJson;

            if (response.TryGetProperty("rtpCapabilities", out var rtpCapabilitiesProp))
            {
                rtpCapabilitiesJson = rtpCapabilitiesProp.GetRawText();
            }
            else if (response.TryGetProperty("recvRtpCapabilities", out var recvProp))
            {
                rtpCapabilitiesJson = recvProp.GetRawText();
            }
            else
            {
                throw new InvalidOperationException("Bridge load_device response does not contain rtpCapabilities.");
            }

            RecvRtpCapabilitiesJson = rtpCapabilitiesJson;

            return new DeviceLoadResult(
                rtpCapabilitiesJson,
                rtpCapabilitiesJson);
        }

        public Task CreateSendTransportAsync(WebRtcTransportInfo info, CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["transportId"] = info.TransportId,
                ["iceParameters"] = JsonSerializer.Deserialize<JsonElement>(info.IceParametersJson),
                ["iceCandidates"] = JsonSerializer.Deserialize<JsonElement>(info.IceCandidatesJson),
                ["dtlsParameters"] = JsonSerializer.Deserialize<JsonElement>(info.DtlsParametersJson)
            };

            return SendVoidRequestAsync("create_send_transport", payload, cancellationToken);
        }

        public Task CreateRecvTransportAsync(WebRtcTransportInfo info, CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["transportId"] = info.TransportId,
                ["iceParameters"] = JsonSerializer.Deserialize<JsonElement>(info.IceParametersJson),
                ["iceCandidates"] = JsonSerializer.Deserialize<JsonElement>(info.IceCandidatesJson),
                ["dtlsParameters"] = JsonSerializer.Deserialize<JsonElement>(info.DtlsParametersJson)
            };

            return SendVoidRequestAsync("create_recv_transport", payload, cancellationToken);
        }

        public Task StartMicrophoneAsync(string serverProducerId, CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["serverProducerId"] = serverProducerId
            };

            return SendVoidRequestAsync("start_microphone", payload, cancellationToken);
        }
        public Task StartCameraAsync(string serverProducerId, CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["serverProducerId"] = serverProducerId
            };

            return SendVoidRequestAsync("start_camera", payload, cancellationToken);
        }

        public Task StopCameraAsync(CancellationToken cancellationToken = default)
        {
            return SendVoidRequestAsync("stop_camera", new Dictionary<string, object?>(), cancellationToken);
        }

        public Task StartScreenShareAsync(string serverProducerId, CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["serverProducerId"] = serverProducerId
            };

            return SendVoidRequestAsync("start_screen", payload, cancellationToken);
        }

        public Task StopScreenShareAsync(CancellationToken cancellationToken = default)
        {
            return SendVoidRequestAsync("stop_screen", new Dictionary<string, object?>(), cancellationToken);
        }

        public Task ConsumeRemoteTrackAsync(ConsumerInfo info, CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["consumerId"] = info.ConsumerId,
                ["producerId"] = info.ProducerId,
                ["kind"] = info.Kind,
                ["trackType"] = info.TrackType,
                ["rtpParameters"] = JsonSerializer.Deserialize<JsonElement>(info.RtpParametersJson)
            };
            var command = string.Equals(info.Kind, "audio", StringComparison.OrdinalIgnoreCase)
                          && !string.Equals(info.TrackType, "camera", StringComparison.OrdinalIgnoreCase)
                          && !string.Equals(info.TrackType, "screen", StringComparison.OrdinalIgnoreCase)
                ? "consume_audio"
                : "consume_video";

            return SendVoidRequestAsync(command, payload, cancellationToken);
        }

        public Task RemoveRemoteConsumerAsync(string consumerId, CancellationToken cancellationToken = default)
        {
            return SendVoidRequestAsync(
                "close_consumer",
                new Dictionary<string, object?>
                {
                    ["consumerId"] = consumerId
                },
                cancellationToken);
        }

        public Task SetMicrophoneEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["enabled"] = enabled
            };

            return SendVoidRequestAsync("set_microphone_enabled", payload, cancellationToken);
        }

        public Task SetCameraEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["enabled"] = enabled
            };

            return SendVoidRequestAsync("set_camera_enabled", payload, cancellationToken);
        }

        public Task SetScreenShareEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["enabled"] = enabled
            };

            return SendVoidRequestAsync("set_screen_enabled", payload, cancellationToken);
        }

        public Task ResolveTransportConnectAsync(string pendingId, bool ok, string? error = null, CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["pendingId"] = pendingId,
                ["ok"] = ok,
                ["error"] = error
            };

            return SendVoidRequestAsync("resolve_transport_connect", payload, cancellationToken);
        }

        public Task ResolveProduceAsync(string pendingId, string serverProducerId, bool ok, string? error = null, CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["pendingId"] = pendingId,
                ["producerId"] = serverProducerId,
                ["ok"] = ok,
                ["error"] = error
            };

            return SendVoidRequestAsync("resolve_transport_produce", payload, cancellationToken);
        }

        public async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            if (_host == null)
                return;

            try
            {
                await SendVoidRequestAsync("close_call", new Dictionary<string, object?>(), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }

            RecvRtpCapabilitiesJson = null;
        }

        private async Task SendVoidRequestAsync(
            string command,
            Dictionary<string, object?> payload,
            CancellationToken cancellationToken)
        {
            var timeoutOverride = string.Equals(command, "consume_video", StringComparison.Ordinal)
                ? ConsumeVideoCommandTimeout
                : (TimeSpan?)null;

            _ = await SendRequestAsync(command, payload, cancellationToken, timeoutOverride).ConfigureAwait(false);
        }

        private void Host_MessageReceived(object? sender, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            BridgeMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<BridgeMessage>(raw, _jsonOptions);
            }
            catch
            {
                if (_hostReadyTcs != null && !IsCompleted(_hostReadyTcs.Task))
                {
                    _hostReadyTcs.TrySetException(
                        new InvalidOperationException("Bridge sent invalid JSON during bootstrap."));
                }

                return;
            }

            if (message == null || string.IsNullOrWhiteSpace(message.Kind))
                return;

            if (string.Equals(message.Kind, "host_ready", StringComparison.Ordinal))
            {
                _hostReadyTcs?.TrySetResult(true);
                return;
            }

            if (string.Equals(message.Kind, "bridge_diag", StringComparison.Ordinal))
                return;

            if (string.Equals(message.Kind, "bridge_error", StringComparison.Ordinal))
            {
                var errorText =
                    message.Payload.ValueKind == JsonValueKind.Object &&
                    message.Payload.TryGetProperty("message", out var msgProp)
                        ? (msgProp.GetString() ?? "Unknown bridge error")
                        : "Unknown bridge error";

                if (!string.IsNullOrWhiteSpace(message.RequestId) &&
                    _pendingResponses.TryRemove(message.RequestId, out var pending))
                {
                    pending.TrySetException(new InvalidOperationException(errorText));
                }
                else if (_hostReadyTcs != null && !IsCompleted(_hostReadyTcs.Task))
                {
                    _hostReadyTcs.TrySetException(
                        new InvalidOperationException("Bridge bootstrap failed: " + errorText));
                }

                return;
            }

            if (string.Equals(message.Kind, "response", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(message.RequestId) &&
                    _pendingResponses.TryRemove(message.RequestId, out var tcs))
                {
                    if (message.Ok)
                        tcs.TrySetResult(message.Payload);
                    else
                        tcs.TrySetException(new InvalidOperationException(message.Error ?? "WebView request failed."));
                }

                return;
            }

            if (string.Equals(message.Kind, "transport_connect", StringComparison.Ordinal))
            {
                _ = HandleTransportConnectAsync(message);
                return;
            }

            if (string.Equals(message.Kind, "transport_produce", StringComparison.Ordinal))
            {
                _ = HandleTransportProduceAsync(message);
                return;
            }
        }

        private async Task HandleTransportConnectAsync(BridgeMessage message)
        {
            var pendingId = message.Payload.TryGetProperty("pendingId", out var pendingProp)
                ? pendingProp.GetString() ?? string.Empty
                : string.Empty;

            var transportId = message.Payload.TryGetProperty("transportId", out var transportProp)
                ? transportProp.GetString() ?? string.Empty
                : string.Empty;

            var direction = message.Payload.TryGetProperty("direction", out var directionProp)
                ? directionProp.GetString() ?? string.Empty
                : string.Empty;

            var dtlsParametersJson = message.Payload.TryGetProperty("dtlsParameters", out var dtlsProp)
                ? dtlsProp.GetRawText()
                : "{}";

            var handler = TransportConnectRequired;
            if (handler == null)
            {
                await ResolveTransportConnectAsync(
                    pendingId,
                    false,
                    "No connect handler registered.").ConfigureAwait(false);
                return;
            }

            var request = new TransportConnectRequest(
                pendingId,
                transportId,
                direction,
                dtlsParametersJson);

            await handler.Invoke(request).ConfigureAwait(false);
        }

        private async Task HandleTransportProduceAsync(BridgeMessage message)
        {
            var pendingId = message.Payload.TryGetProperty("pendingId", out var pendingProp)
                ? pendingProp.GetString() ?? string.Empty
                : string.Empty;

            var transportId = message.Payload.TryGetProperty("transportId", out var transportProp)
                ? transportProp.GetString() ?? string.Empty
                : string.Empty;

            var kind = message.Payload.TryGetProperty("kind", out var kindProp)
                ? kindProp.GetString() ?? "audio"
                : "audio";

            var rtpParametersJson = message.Payload.TryGetProperty("rtpParameters", out var rtpProp)
                ? rtpProp.GetRawText()
                : "{}";

            var serverProducerId = message.Payload.TryGetProperty("serverProducerId", out var producerProp)
                ? producerProp.GetString() ?? string.Empty
                : string.Empty;

            var trackType = message.Payload.TryGetProperty("trackType", out var trackTypeProp)
                ? trackTypeProp.GetString()
                : null;

            var handler = TransportProduceRequired;
            if (handler == null)
            {
                await ResolveProduceAsync(
                    pendingId,
                    serverProducerId,
                    false,
                    "No produce handler registered.").ConfigureAwait(false);
                return;
            }

            var request = new TransportProduceRequest(
                pendingId,
                transportId,
                kind,
                rtpParametersJson,
                serverProducerId,
                trackType);

            await handler.Invoke(request).ConfigureAwait(false);
        }

        private async Task<JsonElement> SendRequestAsync(
            string command,
            Dictionary<string, object?> payload,
            CancellationToken cancellationToken,
            TimeSpan? timeoutOverride = null)
        {
            var host = _host;
            if (host == null)
                throw new InvalidOperationException("Audio bridge host is not attached or not ready.");

            var requestId = Guid.NewGuid().ToString("N");
            var responseTcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingResponses[requestId] = responseTcs;

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (timeoutCts.Token.Register(() =>
            {
                if (_pendingResponses.TryRemove(requestId, out var pending))
                {
                    if (cancellationToken.IsCancellationRequested)
                        pending.TrySetCanceled();
                    else
                        pending.TrySetException(new TimeoutException("Bridge command '" + command + "' timed out."));
                }
            }))
            {
                timeoutCts.CancelAfter(timeoutOverride ?? CommandTimeout);

                try
                {
                    var envelope = new
                    {
                        kind = "command",
                        command = command,
                        requestId = requestId,
                        payload = payload
                    };

                    var json = JsonSerializer.Serialize(envelope, _jsonOptions);

                    await host.PostJsonAsync(json, timeoutCts.Token).ConfigureAwait(false);

                    return await responseTcs.Task.ConfigureAwait(false);
                }
                catch
                {
                    _pendingResponses.TryRemove(requestId, out _);
                    throw;
                }
            }
        }

        private void DetachCurrentHost()
        {
            if (_subscribedHost != null)
            {
                try
                {
                    _subscribedHost.MessageReceived -= Host_MessageReceived;
                }
                catch
                {
                }
            }

            _subscribedHost = null;
            _host = null;
            _hostReadyTcs = null;
            RecvRtpCapabilitiesJson = null;

            FailAllPending(new InvalidOperationException("Audio bridge host was detached."));
        }

        private void FailAllPending(Exception ex)
        {
            foreach (var pair in _pendingResponses)
            {
                if (_pendingResponses.TryRemove(pair.Key, out var pending))
                    pending.TrySetException(ex);
            }
        }

        private static bool IsCompleted(Task task)
        {
            return task != null &&
                   (task.Status == TaskStatus.RanToCompletion ||
                    task.Status == TaskStatus.Faulted ||
                    task.Status == TaskStatus.Canceled);
        }

        private static bool IsRanToCompletion(Task task)
        {
            return task != null && task.Status == TaskStatus.RanToCompletion;
        }

        private sealed class BridgeMessage
        {
            public string Kind { get; set; } = string.Empty;
            public string? RequestId { get; set; }
            public bool Ok { get; set; }
            public string? Error { get; set; }
            public JsonElement Payload { get; set; }
        }
    }
}