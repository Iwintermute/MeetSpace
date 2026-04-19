import * as mediasoupClient from 'mediasoup-client';
const CAMERA_VIDEO_CONSTRAINTS = {
    width: { ideal: 1280, max: 1280 },
    height: { ideal: 720, max: 720 },
    frameRate: { ideal: 30, max: 30 }
};

const SCREEN_VIDEO_CONSTRAINTS = {
    width: { ideal: 1280, max: 1280 },
    height: { ideal: 720, max: 720 },
    frameRate: { ideal: 30, max: 30 }
};

const LOCAL_CAMERA_TILE_KEY = 'local:camera';
const LOCAL_SCREEN_TILE_KEY = 'local:screen';

const state = {
    device: null,
    sendTransport: null,
    recvTransport: null,
    micStream: null,
    micTrack: null,
    micProducer: null,
    cameraStream: null,
    cameraTrack: null,
    cameraProducer: null,
    screenStream: null,
    screenTrack: null,
    screenProducer: null,
    consumers: new Map(),
    audioElements: new Map(),
    videoElements: new Map(),
    videoTiles: new Map(),
    videoTileKeysByConsumer: new Map(),
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

    throw new Error('No supported host bridge found.');
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

function getMediaGridElement() {
    let grid = document.getElementById('media-grid');
    if (!grid) {
        grid = document.createElement('div');
        grid.id = 'media-grid';
        document.body.appendChild(grid);
    }
    return grid;
}

function getTrackTypeLabel(trackType) {
    switch (trackType) {
        case 'screen':
            return 'Экран';
        case 'camera':
            return 'Камера';
        default:
            return 'Видео';
    }
}

function getTileTitle(trackType, isLocal) {
    return `${isLocal ? 'Вы' : 'Участник'} · ${getTrackTypeLabel(trackType)}`;
}

async function applyTrackConstraintsSafe(track, constraints) {
    if (!track || typeof track.applyConstraints !== 'function') {
        return;
    }

    try {
        await track.applyConstraints(constraints);
    } catch (_) {
    }
}

function ensureVideoTile(tileKey, trackType, isLocal) {
    let tile = state.videoTiles.get(tileKey);

    if (!tile) {
        const container = document.createElement('div');
        container.className = 'media-tile';

        const video = document.createElement('video');
        video.autoplay = true;
        video.playsInline = true;
        video.controls = false;
        video.className = 'media-video';

        const label = document.createElement('div');
        label.className = 'media-tile-label';

        container.appendChild(video);
        container.appendChild(label);
        getMediaGridElement().appendChild(container);

        tile = {
            container,
            video,
            label,
            stream: null
        };

        state.videoTiles.set(tileKey, tile);
    }

    tile.container.dataset.trackType = trackType || 'camera';
    tile.container.dataset.scope = isLocal ? 'local' : 'remote';
    tile.label.textContent = getTileTitle(trackType || 'camera', isLocal);
    tile.video.muted = !!isLocal;

    return tile;
}

async function attachVideoTile(tileKey, stream, trackType, isLocal) {
    const tile = ensureVideoTile(tileKey, trackType, isLocal);
    tile.stream = stream;
    tile.video.srcObject = stream;

    try {
        await tile.video.play();
    } catch (_) {
    }
}

function removeVideoTile(tileKey) {
    const tile = state.videoTiles.get(tileKey);
    if (!tile) {
        return;
    }

    try {
        tile.video.pause();
        tile.video.srcObject = null;
    } catch (_) {
    }

    try {
        if (tile.container.parentElement) {
            tile.container.parentElement.removeChild(tile.container);
        }
    } catch (_) {
    }

    state.videoTiles.delete(tileKey);
}

function removeAllVideoTiles() {
    for (const tileKey of Array.from(state.videoTiles.keys())) {
        removeVideoTile(tileKey);
    }

    state.videoTileKeysByConsumer.clear();
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
        if (item.element) {
            item.element.pause();
            item.element.srcObject = null;
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
    state.videoElements.delete(consumerId);

    const tileKey = state.videoTileKeysByConsumer.get(consumerId) || `remote:${consumerId}`;
    removeVideoTile(tileKey);
    state.videoTileKeysByConsumer.delete(consumerId);
}

function cleanupConsumerByProducerId(producerId) {
    if (!producerId) {
        return;
    }

    for (const [consumerId, item] of state.consumers.entries()) {
        const currentProducerId =
            item &&
            item.consumer &&
            item.consumer.appData &&
            item.consumer.appData.producerId
                ? item.consumer.appData.producerId
                : null;

        if (currentProducerId === producerId) {
            cleanupConsumer(consumerId);
        }
    }
}

function stopProducer(name) {
    const producer = state[name];
    if (producer) {
        try {
            producer.close();
        } catch (_) {
        }
    }
    state[name] = null;
}

function stopStream(streamName, trackName) {
    const stream = state[streamName];
    if (stream) {
        try {
            stream.getTracks().forEach(track => track.stop());
        } catch (_) {
        }
    }
    state[streamName] = null;
    state[trackName] = null;
}

async function closeAll() {
    for (const consumerId of Array.from(state.consumers.keys())) {
        cleanupConsumer(consumerId);
    }

    stopProducer('micProducer');
    stopProducer('cameraProducer');
    stopProducer('screenProducer');
    stopStream('micStream', 'micTrack');
    stopStream('cameraStream', 'cameraTrack');
    stopStream('screenStream', 'screenTrack');
    removeVideoTile(LOCAL_CAMERA_TILE_KEY);
    removeVideoTile(LOCAL_SCREEN_TILE_KEY);
    removeAllVideoTiles();

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
                trackType: appData && appData.trackType ? appData.trackType : (kind === 'audio' ? 'microphone' : 'camera'),
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
                stopProducer('micProducer');
                stopStream('micStream', 'micTrack');

                state.micStream = await navigator.mediaDevices.getUserMedia({
                    audio: {
                        echoCancellation: true,
                        noiseSuppression: true,
                        autoGainControl: false,
                        channelCount: { ideal: 1, max: 1 },
                        sampleRate: { ideal: 48000 },
                        sampleSize: { ideal: 16 },
                        latency: { ideal: 0.01 }
                    },
                    video: false
                });

                const audioTracks = state.micStream.getAudioTracks();
                if (!audioTracks || audioTracks.length === 0) {
                    throw new Error('microphone stream has no audio track');
                }

                state.micTrack = audioTracks[0];
                state.micTrack.contentHint = 'speech';
                await applyTrackConstraintsSafe(state.micTrack, {
                    echoCancellation: true,
                    noiseSuppression: true,
                    autoGainControl: false
                });

                const producer = await state.sendTransport.produce({
                    track: state.micTrack,
                    appData: {
                        serverProducerId: payload.serverProducerId,
                        trackType: 'microphone'
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

            case 'start_camera': {
                sendDiag('start_camera.begin');

                ensureDeviceLoaded();

                if (!state.sendTransport) {
                    throw new Error('send transport is not initialized');
                }

                if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
                    throw new Error('mediaDevices.getUserMedia is unavailable');
                }

                stopProducer('cameraProducer');
                stopStream('cameraStream', 'cameraTrack');
                removeVideoTile(LOCAL_CAMERA_TILE_KEY);

                state.cameraStream = await navigator.mediaDevices.getUserMedia({
                    audio: false,
                    video: CAMERA_VIDEO_CONSTRAINTS
                });

                const videoTracks = state.cameraStream.getVideoTracks();
                if (!videoTracks || videoTracks.length === 0) {
                    throw new Error('camera stream has no video track');
                }

                state.cameraTrack = videoTracks[0];
                state.cameraTrack.contentHint = 'motion';
                await applyTrackConstraintsSafe(state.cameraTrack, CAMERA_VIDEO_CONSTRAINTS);
                await attachVideoTile(LOCAL_CAMERA_TILE_KEY, state.cameraStream, 'camera', true);

                const producer = await state.sendTransport.produce({
                    track: state.cameraTrack,
                    appData: {
                        serverProducerId: payload.serverProducerId,
                        trackType: 'camera'
                    }
                });

                state.cameraProducer = producer;
                producer.on('transportclose', () => {
                    state.cameraProducer = null;
                });

                sendDiag('start_camera.ok', {
                    producerId: producer.id
                });

                sendResponse(requestId, true, {
                    producerId: producer.id
                });
                return;
            }

            case 'stop_camera': {
                stopProducer('cameraProducer');
                stopStream('cameraStream', 'cameraTrack');
                removeVideoTile(LOCAL_CAMERA_TILE_KEY);
                sendResponse(requestId, true, {});
                return;
            }

            case 'start_screen': {
                sendDiag('start_screen.begin');

                ensureDeviceLoaded();

                if (!state.sendTransport) {
                    throw new Error('send transport is not initialized');
                }

                if (!navigator.mediaDevices || !navigator.mediaDevices.getDisplayMedia) {
                    throw new Error('mediaDevices.getDisplayMedia is unavailable');
                }

                stopProducer('screenProducer');
                stopStream('screenStream', 'screenTrack');
                removeVideoTile(LOCAL_SCREEN_TILE_KEY);

                state.screenStream = await navigator.mediaDevices.getDisplayMedia({
                    video: SCREEN_VIDEO_CONSTRAINTS,
                    audio: false
                });

                const videoTracks = state.screenStream.getVideoTracks();
                if (!videoTracks || videoTracks.length === 0) {
                    throw new Error('screen stream has no video track');
                }

                state.screenTrack = videoTracks[0];
                state.screenTrack.contentHint = 'detail';
                await applyTrackConstraintsSafe(state.screenTrack, SCREEN_VIDEO_CONSTRAINTS);
                await attachVideoTile(LOCAL_SCREEN_TILE_KEY, state.screenStream, 'screen', true);
                state.screenTrack.onended = () => {
                    stopProducer('screenProducer');
                    stopStream('screenStream', 'screenTrack');
                    removeVideoTile(LOCAL_SCREEN_TILE_KEY);
                };

                const producer = await state.sendTransport.produce({
                    track: state.screenTrack,
                    appData: {
                        serverProducerId: payload.serverProducerId,
                        trackType: 'screen'
                    }
                });

                state.screenProducer = producer;
                producer.on('transportclose', () => {
                    state.screenProducer = null;
                });

                sendDiag('start_screen.ok', {
                    producerId: producer.id
                });

                sendResponse(requestId, true, {
                    producerId: producer.id
                });
                return;
            }

            case 'stop_screen': {
                stopProducer('screenProducer');
                stopStream('screenStream', 'screenTrack');
                removeVideoTile(LOCAL_SCREEN_TILE_KEY);
                sendResponse(requestId, true, {});
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

                sendResponse(requestId, true, { enabled });
                return;
            }

            case 'set_camera_enabled': {
                const enabled = !!payload.enabled;

                if (state.cameraTrack) {
                    state.cameraTrack.enabled = enabled;
                }

                if (state.cameraProducer) {
                    if (enabled) {
                        await state.cameraProducer.resume();
                    } else {
                        await state.cameraProducer.pause();
                    }
                }

                sendResponse(requestId, true, { enabled });
                return;
            }

            case 'set_screen_enabled': {
                const enabled = !!payload.enabled;

                if (state.screenTrack) {
                    state.screenTrack.enabled = enabled;
                }

                if (state.screenProducer) {
                    if (enabled) {
                        await state.screenProducer.resume();
                    } else {
                        await state.screenProducer.pause();
                    }
                }

                sendResponse(requestId, true, { enabled });
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

                cleanupConsumer(payload.consumerId);
                cleanupConsumerByProducerId(payload.producerId);

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
                    element: audio
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

            case 'consume_video': {
                sendDiag('consume_video.begin', {
                    producerId: payload.producerId
                });

                ensureDeviceLoaded();

                if (!state.recvTransport) {
                    throw new Error('receive transport is not initialized');
                }

                cleanupConsumer(payload.consumerId);
                cleanupConsumerByProducerId(payload.producerId);

                const consumer = await state.recvTransport.consume({
                    id: payload.consumerId,
                    producerId: payload.producerId,
                    kind: payload.kind || 'video',
                    rtpParameters: payload.rtpParameters,
                    appData: {
                        producerId: payload.producerId,
                        trackType: payload.trackType || 'camera'
                    }
                });

                const stream = new MediaStream();
                stream.addTrack(consumer.track);
                const trackType = payload.trackType || 'camera';
                const tileKey = `remote:${consumer.id}`;
                state.videoTileKeysByConsumer.set(consumer.id, tileKey);

                let video = state.videoElements.get(consumer.id);

                await attachVideoTile(tileKey, stream, trackType, false);
                const tile = state.videoTiles.get(tileKey);
                if (tile) {
                    video = tile.video;
                    state.videoElements.set(consumer.id, video);
                }

                state.consumers.set(consumer.id, {
                    consumer,
                    stream,
                    element: video
                });

                consumer.on('transportclose', () => cleanupConsumer(consumer.id));
                consumer.on('trackended', () => cleanupConsumer(consumer.id));

                sendDiag('consume_video.ok', {
                    consumerId: consumer.id
                });

                sendResponse(requestId, true, {
                    consumerId: consumer.id
                });
                return;
            }

            case 'close_consumer': {
                if (payload && payload.consumerId) {
                    cleanupConsumer(payload.consumerId);
                }
                sendResponse(requestId, true, {});
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
                    : (hasLegacyHost() ? 'legacy' : 'unknown'),
                href: window.location.href,
                isSecureContext: window.isSecureContext,
                hasMediaDevices: !!navigator.mediaDevices,
                hasGetUserMedia: !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia)
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