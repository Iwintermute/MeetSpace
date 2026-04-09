import * as mediasoupClient from 'mediasoup-client';

const state = {
    device: null,
    sendTransport: null,
    recvTransport: null,
    micStream: null,
    micTrack: null,
    micProducer: null,
    consumers: new Map(),
    audioElements: new Map(),
    pendingConnect: new Map(),
    pendingProduce: new Map()
};

function hasWebView2Host() {
    return !!(
        window.chrome &&
        window.chrome.webview &&
        typeof window.chrome.webview.postMessage === 'function'
    );
}

function hasLegacyHost() {
    return !!(
        window.external &&
        typeof window.external.notify === 'function'
    );
}

function post(message) {
    const raw = JSON.stringify(message);

    if (hasWebView2Host()) {
        window.chrome.webview.postMessage(raw);
        return;
    }

    if (hasLegacyHost()) {
        window.external.notify(raw);
        return;
    }

    throw new Error('No supported host bridge found. Expected chrome.webview or window.external.notify.');
}

function sendResponse(requestId, ok, payload = {}, error = null) {
    post({
        kind: 'response',
        requestId,
        ok,
        error,
        payload
    });
}

function sendBridgeError(requestId, where, error) {
    try {
        post({
            kind: 'bridge_error',
            requestId: requestId || null,
            payload: {
                where: where || 'unknown',
                message: error && error.message ? error.message : String(error),
                stack: error && error.stack ? error.stack : null
            }
        });
    } catch (_) {
    }
}

function sendDiag(step, extra) {
    try {
        post({
            kind: 'bridge_diag',
            payload: Object.assign({ step }, extra || {})
        });
    } catch (_) {
    }
}

function createRequestId() {
    if (
        typeof crypto !== 'undefined' &&
        crypto &&
        typeof crypto.randomUUID === 'function'
    ) {
        return crypto.randomUUID();
    }

    return Math.random().toString(36).slice(2) + Date.now().toString(36);
}

async function createDevice() {
    if (mediasoupClient && typeof mediasoupClient.Device === 'function') {
        return new mediasoupClient.Device();
    }

    if (
        mediasoupClient &&
        mediasoupClient.Device &&
        typeof mediasoupClient.Device.factory === 'function'
    ) {
        return await mediasoupClient.Device.factory();
    }

    throw new Error('mediasoup Device constructor is unavailable');
}

function ensureDeviceLoaded() {
    if (!state.device || !state.device.loaded) {
        throw new Error('mediasoup device is not loaded');
    }
}

function buildTransportOptions(payload) {
    return {
        id: payload.transportId,
        iceParameters: payload.iceParameters,
        iceCandidates: payload.iceCandidates,
        dtlsParameters: payload.dtlsParameters,
        sctpParameters: payload.sctpParameters || undefined,
        appData: {
            transportId: payload.transportId
        }
    };
}

function cleanupConsumer(consumerId) {
    const item = state.consumers.get(consumerId);
    if (!item) {
        return;
    }

    try {
        item.consumer.close();
    } catch (_) {
    }

    try {
        if (item.audio) {
            item.audio.pause();
            item.audio.srcObject = null;
        }
    } catch (_) {
    }

    try {
        if (item.stream) {
            item.stream.getTracks().forEach(track => track.stop());
        }
    } catch (_) {
    }

    state.consumers.delete(consumerId);
    state.audioElements.delete(consumerId);
}

async function closeAll() {
    for (const consumerId of Array.from(state.consumers.keys())) {
        cleanupConsumer(consumerId);
    }

    try {
        if (state.micProducer) {
            state.micProducer.close();
        }
    } catch (_) {
    }
    state.micProducer = null;

    try {
        if (state.micStream) {
            state.micStream.getTracks().forEach(track => track.stop());
        }
    } catch (_) {
    }
    state.micStream = null;
    state.micTrack = null;

    try {
        if (state.sendTransport) {
            state.sendTransport.close();
        }
    } catch (_) {
    }

    try {
        if (state.recvTransport) {
            state.recvTransport.close();
        }
    } catch (_) {
    }

    state.sendTransport = null;
    state.recvTransport = null;
    state.pendingConnect.clear();
    state.pendingProduce.clear();
}

function wireSendTransport(transport) {
    transport.on('connect', ({ dtlsParameters }, callback, errback) => {
        const pendingId = createRequestId();
        state.pendingConnect.set(pendingId, { callback, errback });

        post({
            kind: 'transport_connect',
            payload: {
                pendingId,
                transportId: transport.id,
                direction: 'send',
                dtlsParameters
            }
        });
    });

    transport.on('produce', ({ kind, rtpParameters, appData }, callback, errback) => {
        const pendingId = createRequestId();
        state.pendingProduce.set(pendingId, { callback, errback });

        post({
            kind: 'transport_produce',
            payload: {
                pendingId,
                transportId: transport.id,
                kind,
                rtpParameters,
                serverProducerId: appData && appData.serverProducerId ? appData.serverProducerId : ''
            }
        });
    });
}

function wireRecvTransport(transport) {
    transport.on('connect', ({ dtlsParameters }, callback, errback) => {
        const pendingId = createRequestId();
        state.pendingConnect.set(pendingId, { callback, errback });

        post({
            kind: 'transport_connect',
            payload: {
                pendingId,
                transportId: transport.id,
                direction: 'recv',
                dtlsParameters
            }
        });
    });
}

async function handleCommand(message) {
    const requestId = message.requestId;
    const command = message.command;
    const payload = message.payload || {};

    try {
        switch (command) {
            case 'load_device': {
                sendDiag('load_device.begin');

                state.device = await createDevice();

                await state.device.load({
                    routerRtpCapabilities: payload.routerRtpCapabilities
                });

                if (!state.device.rtpCapabilities) {
                    throw new Error('device.rtpCapabilities is empty after load');
                }

                sendDiag('load_device.ok');

                sendResponse(requestId, true, {
                    rtpCapabilities: state.device.rtpCapabilities
                });
                return;
            }

            case 'create_send_transport': {
                sendDiag('create_send_transport.begin');

                ensureDeviceLoaded();

                if (state.sendTransport) {
                    try {
                        state.sendTransport.close();
                    } catch (_) {
                    }
                }

                state.sendTransport = state.device.createSendTransport(buildTransportOptions(payload));

                if (!state.sendTransport) {
                    throw new Error('createSendTransport returned null');
                }

                wireSendTransport(state.sendTransport);

                sendDiag('create_send_transport.ok', {
                    transportId: state.sendTransport.id
                });

                sendResponse(requestId, true, {
                    transportId: state.sendTransport.id
                });
                return;
            }

            case 'create_recv_transport': {
                sendDiag('create_recv_transport.begin');

                ensureDeviceLoaded();

                if (state.recvTransport) {
                    try {
                        state.recvTransport.close();
                    } catch (_) {
                    }
                }

                state.recvTransport = state.device.createRecvTransport(buildTransportOptions(payload));

                if (!state.recvTransport) {
                    throw new Error('createRecvTransport returned null');
                }

                wireRecvTransport(state.recvTransport);

                sendDiag('create_recv_transport.ok', {
                    transportId: state.recvTransport.id
                });

                sendResponse(requestId, true, {
                    transportId: state.recvTransport.id
                });
                return;
            }

            case 'resolve_transport_connect': {
                const pendingId = payload.pendingId;
                const pending = state.pendingConnect.get(pendingId);

                if (!pending) {
                    sendResponse(requestId, true, {});
                    return;
                }

                state.pendingConnect.delete(pendingId);

                if (payload.ok) {
                    pending.callback();
                } else {
                    pending.errback(new Error(payload.error || 'transport connect rejected'));
                }

                sendResponse(requestId, true, {});
                return;
            }

            case 'resolve_transport_produce': {
                const pendingId = payload.pendingId;
                const pending = state.pendingProduce.get(pendingId);

                if (!pending) {
                    sendResponse(requestId, true, {});
                    return;
                }

                state.pendingProduce.delete(pendingId);

                if (payload.ok) {
                    pending.callback({ id: payload.producerId });
                } else {
                    pending.errback(new Error(payload.error || 'transport produce rejected'));
                }

                sendResponse(requestId, true, {});
                return;
            }

            case 'start_microphone': {
                sendDiag('start_microphone.begin');

                ensureDeviceLoaded();

                if (!state.sendTransport) {
                    throw new Error('send transport is not initialized');
                }

                if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
                    throw new Error('mediaDevices.getUserMedia is unavailable');
                }

                if (state.micProducer) {
                    try {
                        state.micProducer.close();
                    } catch (_) {
                    }
                    state.micProducer = null;
                }

                if (state.micStream) {
                    try {
                        state.micStream.getTracks().forEach(track => track.stop());
                    } catch (_) {
                    }
                    state.micStream = null;
                }

                state.micStream = await navigator.mediaDevices.getUserMedia({
                    audio: {
                        echoCancellation: true,
                        noiseSuppression: true,
                        autoGainControl: true
                    },
                    video: false
                });

                const audioTracks = state.micStream.getAudioTracks();
                if (!audioTracks || audioTracks.length === 0) {
                    throw new Error('microphone stream has no audio track');
                }

                state.micTrack = audioTracks[0];

                const producer = await state.sendTransport.produce({
                    track: state.micTrack,
                    appData: {
                        serverProducerId: payload.serverProducerId
                    }
                });

                state.micProducer = producer;

                producer.on('transportclose', () => {
                    state.micProducer = null;
                });

                sendDiag('start_microphone.ok', {
                    producerId: producer.id
                });

                sendResponse(requestId, true, {
                    producerId: producer.id
                });
                return;
            }

            case 'set_microphone_enabled': {
                const enabled = !!payload.enabled;

                if (state.micTrack) {
                    state.micTrack.enabled = enabled;
                }

                if (state.micProducer) {
                    if (enabled) {
                        await state.micProducer.resume();
                    } else {
                        await state.micProducer.pause();
                    }
                }

                sendResponse(requestId, true, {
                    enabled
                });
                return;
            }

            case 'consume_audio': {
                sendDiag('consume_audio.begin', {
                    producerId: payload.producerId
                });

                ensureDeviceLoaded();

                if (!state.recvTransport) {
                    throw new Error('receive transport is not initialized');
                }

                const consumer = await state.recvTransport.consume({
                    id: payload.consumerId,
                    producerId: payload.producerId,
                    kind: payload.kind,
                    rtpParameters: payload.rtpParameters,
                    appData: {
                        producerId: payload.producerId
                    }
                });

                const stream = new MediaStream();
                stream.addTrack(consumer.track);

                let audio = state.audioElements.get(consumer.id);
                if (!audio) {
                    audio = new Audio();
                    audio.autoplay = true;
                    audio.playsInline = true;
                    audio.volume = 1.0;
                    state.audioElements.set(consumer.id, audio);
                }

                audio.srcObject = stream;

                try {
                    await audio.play();
                } catch (_) {
                }

                state.consumers.set(consumer.id, {
                    consumer,
                    stream,
                    audio
                });

                consumer.on('transportclose', () => cleanupConsumer(consumer.id));
                consumer.on('trackended', () => cleanupConsumer(consumer.id));

                sendDiag('consume_audio.ok', {
                    consumerId: consumer.id
                });

                sendResponse(requestId, true, {
                    consumerId: consumer.id
                });
                return;
            }

            case 'close_call': {
                await closeAll();
                sendResponse(requestId, true, {});
                return;
            }

            default:
                throw new Error(`Unknown command: ${command}`);
        }
    } catch (error) {
        sendDiag(command + '.failed', {
            message: error && error.message ? error.message : String(error)
        });

        sendResponse(
            requestId,
            false,
            {},
            error && error.message ? error.message : String(error));
    }
}
function notifyHostReady() {
    try {
        post({
            kind: 'host_ready',
            payload: {
                transport: hasWebView2Host()
                    ? 'webview2'
                    : (hasLegacyHost() ? 'legacy' : 'unknown'),
                href: window.location.href,
                isSecureContext: window.isSecureContext,
                hasMediaDevices: !!(navigator.mediaDevices),
                hasGetUserMedia: !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia)
            }
        });
    } catch (error) {
        sendBridgeError(null, 'notifyHostReady', error);
    }
}
function handleIncomingHostRaw(rawJson) {
    let requestId = null;

    try {
        const parsed = typeof rawJson === 'string' ? JSON.parse(rawJson) : rawJson;
        requestId = parsed && parsed.requestId ? parsed.requestId : null;
    } catch (_) {
    }

    Promise.resolve()
        .then(() => {
            const message = typeof rawJson === 'string' ? JSON.parse(rawJson) : rawJson;
            return handleCommand(message);
        })
        .catch((error) => {
            sendBridgeError(requestId, 'handleIncomingHostRaw', error);
        });
}

function registerBridge() {
    window.meetspaceBridge = window.meetspaceBridge || {};

    window.meetspaceBridgeReadyState = function () {
        return 'ready';
    };

    window.meetspaceBridge.receive = function (rawJson) {
        handleIncomingHostRaw(rawJson);
        return 'ok';
    };

    if (
        hasWebView2Host() &&
        typeof window.chrome.webview.addEventListener === 'function'
    ) {
        window.chrome.webview.addEventListener('message', (event) => {
            const raw = typeof event.data === 'string'
                ? event.data
                : JSON.stringify(event.data);

            handleIncomingHostRaw(raw);
        });
    }
}

function notifyHostReady() {
    try {
        post({
            kind: 'host_ready',
            payload: {
                transport: hasWebView2Host()
                    ? 'webview2'
                    : (hasLegacyHost() ? 'legacy' : 'unknown')
            }
        });
    } catch (error) {
        sendBridgeError(null, 'notifyHostReady', error);
    }
}

window.addEventListener('error', (event) => {
    try {
        const error = event && event.error
            ? event.error
            : new Error(event && event.message ? event.message : 'Unknown window error');

        sendBridgeError(null, 'window.error', error);
    } catch (_) {
    }
});

window.addEventListener('unhandledrejection', (event) => {
    try {
        const reason = event && event.reason
            ? event.reason
            : new Error('Unhandled promise rejection');

        const error = reason instanceof Error
            ? reason
            : new Error(typeof reason === 'string' ? reason : JSON.stringify(reason));

        sendBridgeError(null, 'window.unhandledrejection', error);
    } catch (_) {
    }
});

registerBridge();

if (document.readyState === 'complete') {
    setTimeout(notifyHostReady, 0);
} else {
    window.addEventListener('load', () => {
        setTimeout(notifyHostReady, 0);
    });
}