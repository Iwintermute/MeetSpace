import * as mediasoupClient from 'mediasoup-client';
const CAMERA_VIDEO_CONSTRAINTS_HD = {
    width: { ideal: 1920, max: 1920 },
    height: { ideal: 1080, max: 1080 },
    frameRate: { ideal: 30, max: 30 }
};
const CAMERA_VIDEO_CONSTRAINTS_BALANCED = {
    width: { ideal: 1920, max: 1920 },
    height: { ideal: 1080, max: 1080 },
    frameRate: { ideal: 30, max: 30 }
};
const CAMERA_VIDEO_CONSTRAINTS = CAMERA_VIDEO_CONSTRAINTS_HD;

const MICROPHONE_AUDIO_CONSTRAINTS_HD = {
    echoCancellation: true,
    noiseSuppression: true,
    autoGainControl: true,
    channelCount: { ideal: 1 },
    sampleRate: { ideal: 48000 },
    sampleSize: { ideal: 16 }
};
const MICROPHONE_AUDIO_CONSTRAINTS_FALLBACK = {
    echoCancellation: true,
    noiseSuppression: true,
    autoGainControl: true,
    channelCount: { ideal: 1 },
    sampleRate: { ideal: 48000 },
    sampleSize: { ideal: 16 }
};
const MICROPHONE_PRODUCER_CODEC_OPTIONS = {
    opusStereo: false,
    opusFec: true,
    opusDtx: true,
    opusMaxPlaybackRate: 48000,
    opusMaxAverageBitrate: 128000,
    opusPtime: 20
};

const CAMERA_SIMULCAST_ENCODINGS = [
    { scaleResolutionDownBy: 4, maxBitrate: 500000 },
    { scaleResolutionDownBy: 2, maxBitrate: 1500000 },
    { scaleResolutionDownBy: 1, maxBitrate: 5000000 }
];
const CAMERA_SINGLE_LAYER_ENCODING = [
    { maxBitrate: 3500000, maxFramerate: 30 }
];

const SCREEN_PRODUCER_ENCODING = [
    { maxBitrate: 5000000, maxFramerate: 30 }
];

const SCREEN_VIDEO_CONSTRAINTS_HD = {
    width: { ideal: 1920, max: 1920 },
    height: { ideal: 1080, max: 1080 },
    frameRate: { ideal: 30, max: 30 }
};
const SCREEN_VIDEO_CONSTRAINTS_BALANCED = {
    width: { ideal: 1280, max: 1280 },
    height: { ideal: 720, max: 720 },
    frameRate: { ideal: 30, max: 30 }
};
const SCREEN_VIDEO_CONSTRAINTS = SCREEN_VIDEO_CONSTRAINTS_HD;
const CAMERA_FALLBACK_CONSTRAINTS = [
    CAMERA_VIDEO_CONSTRAINTS_HD,
    CAMERA_VIDEO_CONSTRAINTS_BALANCED,
    {
        width: { ideal: 1920, max: 1920 },
        height: { ideal: 1080, max: 1080 },
        frameRate: { ideal: 30, max: 30 }
    },
    {
        width: { ideal: 1920, max: 1920 },
        height: { ideal: 1080, max: 1080 },
        frameRate: { ideal: 24, max: 30 }
    },
    {
        width: { ideal: 1920, max: 1920 },
        height: { ideal: 1080, max: 1080 },
        frameRate: { ideal: 24, max: 30 }
    },
    true
];
const SCREEN_FALLBACK_CONSTRAINTS = [
    SCREEN_VIDEO_CONSTRAINTS_HD,
    SCREEN_VIDEO_CONSTRAINTS_BALANCED,
    {
        width: { ideal: 1920, max: 1920 },
        height: { ideal: 1080, max: 1080 },
        frameRate: { ideal: 24, max: 30 }
    },
    true
];
const MICROPHONE_FALLBACK_CONSTRAINTS = [
    MICROPHONE_AUDIO_CONSTRAINTS_HD,
    MICROPHONE_AUDIO_CONSTRAINTS_FALLBACK,
    true
];
const MEDIA_PLAY_TIMEOUT_MS = 1500;
const BRIDGE_DIAGNOSTICS_ENABLED = false;

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
    stageContainer: null,
    stripContainer: null,
    focusedTileKey: null,
    pendingConnect: new Map(),
    pendingProduce: new Map(),
    pendingMediaStarts: new Map(),
    pendingMediaToggles: new Map()
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

function isConstraintError(error) {
    if (!error)
        return false;

    const name = error.name || '';
    return name === 'OverconstrainedError' || name === 'ConstraintNotSatisfiedError';
}

function isScreenSelectionCancelled(error) {
    if (!error)
        return false;

    const name = error.name || '';
    return name === 'AbortError' || name === 'NotAllowedError';
}
function isMediaSourceBusyError(error) {
    if (!error)
        return false;

    const name = error.name || '';
    return name === 'NotReadableError' || name === 'TrackStartError';
}

function delay(ms) {
    return new Promise((resolve) => {
        setTimeout(resolve, ms);
    });
}

async function ensureCapturePromptFocus() {
    try {
        window.focus();
    } catch (_) {
    }

    if (typeof document !== 'undefined') {
        try {
            if (typeof document.hasFocus === 'function' && !document.hasFocus())
                await delay(120);
        } catch (_) {
        }

        try {
            if (document.visibilityState && document.visibilityState !== 'visible')
                await delay(120);
        } catch (_) {
        }
    }

    await delay(60);
}

function stopTracksInStream(stream) {
    if (!stream || typeof stream.getTracks !== 'function')
        return;

    try {
        stream.getTracks().forEach((track) => {
            try {
                track.stop();
            } catch (_) {
            }
        });
    } catch (_) {
    }
}

function isLiveTrack(track) {
    return !!track && track.readyState === 'live';
}

function createTrackStartError(message) {
    const error = new Error(message || 'Media track did not become active.');
    error.name = 'TrackStartError';
    return error;
}

function ensureLiveCaptureTrack(stream, kind) {
    if (!stream)
        throw createTrackStartError('Capture stream is unavailable.');

    const track = kind === 'audio'
        ? stream.getAudioTracks()[0]
        : stream.getVideoTracks()[0];

    if (!isLiveTrack(track)) {
        stopTracksInStream(stream);
        throw createTrackStartError(
            kind === 'audio'
                ? 'Microphone track did not become active.'
                : 'Video capture track did not become active.');
    }

    return track;
}

function normalizeMediaStartError(scope, error) {
    if (!error)
        return new Error(`Could not start ${scope}.`);

    const name = error.name || '';
    if (scope === 'screen' && isScreenSelectionCancelled(error))
        return new Error('Screen sharing was cancelled.');
    if (name === 'NotAllowedError')
        return new Error(`Permission denied for ${scope}.`);
    if (name === 'NotFoundError')
        return new Error(`${scope} source is unavailable.`);
    if (name === 'NotReadableError' || name === 'TrackStartError')
        return scope === 'screen'
            ? new Error('Screen capture could not be started by WebView. Open screen share again and choose a source from the system picker.')
            : new Error(`${scope} source is busy or blocked by another app.`);
    if (name === 'InvalidStateError')
        return new Error(`${scope} cannot be started in the current state.`);

    return error instanceof Error
        ? error
        : new Error(typeof error === 'string' ? error : `Could not start ${scope}.`);
}

async function applyTrackConstraintsWithFallback(track, fallbackConstraints, diagEventName) {
    if (!track || typeof track.applyConstraints !== 'function')
        return;

    for (const constraints of fallbackConstraints) {
        if (!constraints || constraints === true)
            continue;

        try {
            await track.applyConstraints(constraints);
            return;
        } catch (error) {
            sendDiag(diagEventName, {
                message: error && error.message ? error.message : String(error)
            });

            if (!isConstraintError(error))
                return;
        }
    }
}

function sendDiag(step, extra) {
    if (!BRIDGE_DIAGNOSTICS_ENABLED) {
        return;
    }
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

function normalizeVideoTrackType(trackType) {
    if (!trackType)
        return 'camera';

    const normalized = trackType.toString().trim().toLowerCase();
    if (normalized === 'screen_share' || normalized === 'screenshare' || normalized === 'display')
        return 'screen';
    if (normalized === 'video')
        return 'camera';

    return normalized;
}

function resolveConsumeKind(kind, trackType) {
    const normalizedTrackType = normalizeVideoTrackType(trackType);
    if (normalizedTrackType === 'screen' || normalizedTrackType === 'camera')
        return 'video';

    const normalizedKind = (kind || '').toString().trim().toLowerCase();
    if (
        normalizedKind === 'video' ||
        normalizedKind === 'camera' ||
        normalizedKind === 'screen' ||
        normalizedKind === 'screen_share' ||
        normalizedKind === 'screenshare'
    ) {
        return 'video';
    }

    return 'audio';
}

function normalizeCodecMimeType(codec) {
    if (!codec || !codec.mimeType)
        return '';

    return codec.mimeType.toString().trim().toLowerCase();
}

function getDeviceVideoCodecs() {
    if (!state.device || !state.device.rtpCapabilities || !Array.isArray(state.device.rtpCapabilities.codecs))
        return [];

    return state.device.rtpCapabilities.codecs.filter((codec) => {
        const mimeType = normalizeCodecMimeType(codec);
        return mimeType.startsWith('video/');
    });
}

function pickPreferredVideoCodec(preferredMimeTypes) {
    const codecs = getDeviceVideoCodecs();
    if (!Array.isArray(preferredMimeTypes) || preferredMimeTypes.length === 0)
        return null;

    for (const preferredMimeType of preferredMimeTypes) {
        const normalizedPreferred = (preferredMimeType || '').toString().trim().toLowerCase();
        if (!normalizedPreferred)
            continue;

        const codec = codecs.find((item) => normalizeCodecMimeType(item) === normalizedPreferred);
        if (codec)
            return codec;
    }

    return null;
}

function buildCameraProducerProfiles() {
    const profiles = [];
    const dedupe = new Set();
    const pushProfile = (label, codec, encodings) => {
        const mimeType = normalizeCodecMimeType(codec) || 'auto';
        const encodingMode = Array.isArray(encodings) && encodings.length > 1 ? 'simulcast' : 'single';
        const key = `${mimeType}:${encodingMode}`;
        if (dedupe.has(key))
            return;

        dedupe.add(key);
        profiles.push({ label, codec, encodings });
    };

    const vp8Codec = pickPreferredVideoCodec(['video/vp8']);
    const h264Codec = pickPreferredVideoCodec(['video/h264']);
    const vp9Codec = pickPreferredVideoCodec(['video/vp9']);
    if (vp8Codec)
        pushProfile('vp8-simulcast', vp8Codec, CAMERA_SIMULCAST_ENCODINGS);
    if (h264Codec)
        pushProfile('h264-simulcast', h264Codec, CAMERA_SIMULCAST_ENCODINGS);
    if (vp9Codec)
        pushProfile('vp9-single', vp9Codec, CAMERA_SINGLE_LAYER_ENCODING);
    pushProfile('auto-simulcast', null, CAMERA_SIMULCAST_ENCODINGS);
    pushProfile('auto-single', null, CAMERA_SINGLE_LAYER_ENCODING);

    return profiles;
}

function buildScreenProducerProfiles() {
    const profiles = [];
    const dedupe = new Set();
    const pushProfile = (label, codec) => {
        const mimeType = normalizeCodecMimeType(codec) || 'auto';
        if (dedupe.has(mimeType))
            return;

        dedupe.add(mimeType);
        profiles.push({
            label,
            codec,
            encodings: SCREEN_PRODUCER_ENCODING
        });
    };

    const vp9Codec = pickPreferredVideoCodec(['video/vp9']);
    const vp8Codec = pickPreferredVideoCodec(['video/vp8']);
    const h264Codec = pickPreferredVideoCodec(['video/h264']);
    if (vp9Codec)
        pushProfile('vp9-single', vp9Codec);
    if (vp8Codec)
        pushProfile('vp8-single', vp8Codec);
    if (h264Codec)
        pushProfile('h264-single', h264Codec);
    pushProfile('auto-single', null);

    return profiles;
}

function buildVideoProducerOptions(track, payload, trackType, profile) {
    const options = {
        track,
        encodings: profile.encodings,
        codecOptions: {
            videoGoogleStartBitrate: 2000
        },
        appData: {
            serverProducerId: payload.serverProducerId,
            trackType
        }
    };

    if (profile.codec)
        options.codec = profile.codec;

    return options;
}

async function produceVideoWithFallback(track, payload, trackType, profiles, diagScope) {
    let lastError = null;
    for (const profile of profiles) {
        try {
            const options = buildVideoProducerOptions(track, payload, trackType, profile);
            return await state.sendTransport.produce(options);
        } catch (error) {
            lastError = error;
            sendDiag(`${diagScope}.profile_failed`, {
                profile: profile.label,
                message: error && error.message ? error.message : String(error)
            });
        }
    }

    throw lastError || new Error(`Could not start ${trackType}.`);
}

function ensureMediaLayout() {
    const grid = getMediaGridElement();

    if (!state.stageContainer) {
        const stage = document.createElement('div');
        stage.className = 'media-stage';
        grid.appendChild(stage);
        state.stageContainer = stage;
    }

    if (!state.stripContainer) {
        const strip = document.createElement('div');
        strip.className = 'media-strip';
        grid.appendChild(strip);
        state.stripContainer = strip;
    }

    return {
        grid,
        stage: state.stageContainer,
        strip: state.stripContainer
    };
}

function syncTileLayout() {
    const { grid, stage, strip } = ensureMediaLayout();
    const hasFocus = !!(state.focusedTileKey && state.videoTiles.has(state.focusedTileKey));

    grid.dataset.hasFocus = hasFocus ? 'true' : 'false';

    for (const [tileKey, tile] of state.videoTiles.entries()) {
        const shouldFocus = hasFocus && tileKey === state.focusedTileKey;
        if (shouldFocus) {
            stage.appendChild(tile.container);
            tile.container.classList.add('media-tile--focused');
        } else {
            strip.appendChild(tile.container);
            tile.container.classList.remove('media-tile--focused');
        }
    }
}

function setFocusedTile(tileKey) {
    if (tileKey && !state.videoTiles.has(tileKey))
        state.focusedTileKey = null;
    else
        state.focusedTileKey = tileKey || null;

    syncTileLayout();
}
function canUseFullscreenApi() {
    return typeof document !== 'undefined' &&
        typeof document.exitFullscreen === 'function';
}

async function requestGridFullscreen() {
    const grid = getMediaGridElement();
    if (!grid || typeof grid.requestFullscreen !== 'function')
        return false;

    if (document.fullscreenElement === grid)
        return true;

    try {
        await grid.requestFullscreen();
        return true;
    } catch (_) {
        return false;
    }
}

async function exitGridFullscreen() {
    if (!canUseFullscreenApi())
        return;

    if (!document.fullscreenElement)
        return;

    try {
        await document.exitFullscreen();
    } catch (_) {
    }
}


async function toggleMediaStageFocus(tileKey) {
    if (!tileKey)
        return;

    if (state.focusedTileKey === tileKey) {
        setFocusedTile(null);
        await exitGridFullscreen();
        return;
    }

    setFocusedTile(tileKey);
    await requestGridFullscreen();
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
    const normalizedTrackType = normalizeVideoTrackType(trackType);
    const { strip } = ensureMediaLayout();
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
        container.addEventListener('dblclick', async (event) =>
        {
            if (typeof event.button === 'number' && event.button !== 0)
                return;
            await toggleMediaStageFocus(tileKey);
        });
        strip.appendChild(container);

        tile = {
            container,
            video,
            label,
            stream: null
        };

        state.videoTiles.set(tileKey, tile);
    }

    tile.container.dataset.trackType = normalizedTrackType;
    tile.container.dataset.scope = isLocal ? 'local' : 'remote';
    tile.label.textContent = getTileTitle(normalizedTrackType, isLocal);
    tile.video.muted = !!isLocal;

    return tile;
}


async function playMediaElementSafe(element, timeoutMs = MEDIA_PLAY_TIMEOUT_MS) {
    if (!element || typeof element.play !== 'function') {
        return;
    }

    try {
        const playPromise = element.play();
        if (playPromise && typeof playPromise.then === 'function') {
            await Promise.race([
                playPromise,
                new Promise((resolve) => {
                    setTimeout(resolve, timeoutMs);
                })
            ]);
        }
    } catch (_) {
    }
}

async function openCameraStreamWithFallback() {
    try {
        const stream = await navigator.mediaDevices.getUserMedia({
            audio: false,
            video: true
        });
        const videoTrack = ensureLiveCaptureTrack(stream, 'video');
        await applyTrackConstraintsWithFallback(
            videoTrack,
            CAMERA_FALLBACK_CONSTRAINTS,
            'start_camera.constraints_retry');

        return stream;
    } catch (error) {
        throw normalizeMediaStartError('camera', error);
    }
}

async function openMicrophoneStreamWithFallback() {
    try {
        const stream = await navigator.mediaDevices.getUserMedia({
            audio: true,
            video: false
        });
        const audioTrack = ensureLiveCaptureTrack(stream, 'audio');
        await applyTrackConstraintsWithFallback(
            audioTrack,
            MICROPHONE_FALLBACK_CONSTRAINTS,
            'start_microphone.constraints_retry');

        return stream;
    } catch (error) {
        throw normalizeMediaStartError('microphone', error);
    }
}

async function openScreenStreamWithFallback() {
    let lastError = null;
    const displayMediaOptions = [
        {
            mode: 'monitor-hint',
            constraints: {
                video: {
                    displaySurface: 'monitor'
                },
                audio: false
            }
        },
        {
            mode: 'plain-video',
            constraints: {
                video: true,
                audio: false
            }
        }
    ];

    for (let attempt = 0; attempt < displayMediaOptions.length; attempt++) {
        const option = displayMediaOptions[attempt];
        try {
            await ensureCapturePromptFocus();
            const stream = await navigator.mediaDevices.getDisplayMedia(option.constraints);

            const videoTrack = ensureLiveCaptureTrack(stream, 'video');
            await applyTrackConstraintsWithFallback(
                videoTrack,
                SCREEN_FALLBACK_CONSTRAINTS,
                'start_screen.constraints_retry');

            return stream;
        } catch (error) {
            lastError = error;
            sendDiag('start_screen.capture_attempt_failed', {
                mode: option.mode,
                message: error && error.message ? error.message : String(error)
            });
            if (error && error.name === 'TypeError')
                continue;

            if (isScreenSelectionCancelled(error))
                break;

            if (isMediaSourceBusyError(error))
                await delay(350);
        }
    }

    throw normalizeMediaStartError('screen', lastError);
}

async function resetLocalScreenCaptureState() {
    stopProducer('screenProducer');
    stopStream('screenStream', 'screenTrack');
    removeVideoTile(LOCAL_SCREEN_TILE_KEY);
    await delay(120);
}
async function attachVideoTile(tileKey, stream, trackType, isLocal) {
    const normalizedTrackType = normalizeVideoTrackType(trackType);
    const tile = ensureVideoTile(tileKey, normalizedTrackType, isLocal);
    tile.stream = stream;
    tile.video.srcObject = stream;

    await playMediaElementSafe(tile.video);

    if (normalizedTrackType === 'screen') {
        setFocusedTile(tileKey);
        void requestGridFullscreen();
    } else {
        syncTileLayout();
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
    if (state.focusedTileKey === tileKey) {
        state.focusedTileKey = null;
        if (document.fullscreenElement) {
            void exitGridFullscreen();
        }
    }

    syncTileLayout();
}

function removeAllVideoTiles() {
    for (const tileKey of Array.from(state.videoTiles.keys())) {
        removeVideoTile(tileKey);
    }
    state.focusedTileKey = null;

    state.videoTileKeysByConsumer.clear();
    syncTileLayout();
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
        stopTracksInStream(stream);
    }
    state[streamName] = null;
    state[trackName] = null;
}

function bindMicrophoneTrackLifecycle(track) {
    if (!track)
        return;

    track.onended = () => {
        stopStream('micStream', 'micTrack');
    };
}

function bindCameraTrackLifecycle(track) {
    if (!track)
        return;

    track.onended = () => {
        stopStream('cameraStream', 'cameraTrack');
        removeVideoTile(LOCAL_CAMERA_TILE_KEY);
    };
}

function bindScreenTrackLifecycle(track) {
    if (!track)
        return;

    track.onended = () => {
        stopStream('screenStream', 'screenTrack');
        removeVideoTile(LOCAL_SCREEN_TILE_KEY);

        if (state.screenProducer && !state.screenProducer.closed && !state.screenProducer.paused) {
            Promise.resolve(state.screenProducer.pause()).catch(() => {
            });
        }
    };
}

function wireLocalProducerLifecycle(producerKey, onInvalidated) {
    const producer = state[producerKey];
    if (!producer)
        return;

    const invalidate = () => {
        if (state[producerKey] !== producer)
            return;

        state[producerKey] = null;
        if (typeof onInvalidated === 'function')
            onInvalidated();
    };

    producer.on('transportclose', invalidate);
    producer.on('close', invalidate);
}

async function runMediaToggleGuard(operationKey, operation) {
    const inFlight = state.pendingMediaToggles.get(operationKey);
    if (inFlight)
        return await inFlight;

    const guardedPromise = (async () => {
        try {
            return await operation();
        } finally {
            state.pendingMediaToggles.delete(operationKey);
        }
    })();

    state.pendingMediaToggles.set(operationKey, guardedPromise);
    return await guardedPromise;
}

async function ensureMicrophoneTrackForProducer() {
    if (isLiveTrack(state.micTrack) && state.micStream)
        return;

    const previousStream = state.micStream;
    const replacementStream = await openMicrophoneStreamWithFallback();
    const replacementTracks = replacementStream.getAudioTracks();
    if (!replacementTracks || replacementTracks.length === 0) {
        stopTracksInStream(replacementStream);
        throw new Error('microphone stream has no audio track');
    }

    const replacementTrack = replacementTracks[0];
    replacementTrack.contentHint = 'speech';
    bindMicrophoneTrackLifecycle(replacementTrack);

    try {
        if (state.micProducer && typeof state.micProducer.replaceTrack === 'function')
            await state.micProducer.replaceTrack({ track: replacementTrack });
    } catch (error) {
        stopTracksInStream(replacementStream);
        throw error;
    }

    state.micStream = replacementStream;
    state.micTrack = replacementTrack;
    if (previousStream && previousStream !== replacementStream)
        stopTracksInStream(previousStream);
}

async function ensureCameraTrackForProducer() {
    if (isLiveTrack(state.cameraTrack) && state.cameraStream)
        return;

    const previousStream = state.cameraStream;
    const replacementStream = await openCameraStreamWithFallback();
    const replacementTracks = replacementStream.getVideoTracks();
    if (!replacementTracks || replacementTracks.length === 0) {
        stopTracksInStream(replacementStream);
        throw new Error('camera stream has no video track');
    }

    const replacementTrack = replacementTracks[0];
    replacementTrack.contentHint = 'motion';
    await applyTrackConstraintsSafe(replacementTrack, CAMERA_VIDEO_CONSTRAINTS);
    bindCameraTrackLifecycle(replacementTrack);

    try {
        if (state.cameraProducer && typeof state.cameraProducer.replaceTrack === 'function')
            await state.cameraProducer.replaceTrack({ track: replacementTrack });
    } catch (error) {
        stopTracksInStream(replacementStream);
        throw error;
    }

    state.cameraStream = replacementStream;
    state.cameraTrack = replacementTrack;
    await attachVideoTile(LOCAL_CAMERA_TILE_KEY, replacementStream, 'camera', true);
    if (previousStream && previousStream !== replacementStream)
        stopTracksInStream(previousStream);
}

async function ensureScreenTrackForProducer() {
    if (isLiveTrack(state.screenTrack) && state.screenStream)
        return;

    const previousStream = state.screenStream;
    const replacementStream = await openScreenStreamWithFallback();
    const replacementTracks = replacementStream.getVideoTracks();
    if (!replacementTracks || replacementTracks.length === 0) {
        stopTracksInStream(replacementStream);
        throw new Error('screen stream has no video track');
    }

    const replacementTrack = replacementTracks[0];
    replacementTrack.contentHint = 'detail';
    await applyTrackConstraintsSafe(replacementTrack, SCREEN_VIDEO_CONSTRAINTS);
    bindScreenTrackLifecycle(replacementTrack);

    try {
        if (state.screenProducer && typeof state.screenProducer.replaceTrack === 'function')
            await state.screenProducer.replaceTrack({ track: replacementTrack });
    } catch (error) {
        stopTracksInStream(replacementStream);
        throw error;
    }

    state.screenStream = replacementStream;
    state.screenTrack = replacementTrack;
    await attachVideoTile(LOCAL_SCREEN_TILE_KEY, replacementStream, 'screen', true);
    if (previousStream && previousStream !== replacementStream)
        stopTracksInStream(previousStream);
}

async function runMediaStartGuard(operationKey, operation) {
    const inFlight = state.pendingMediaStarts.get(operationKey);
    if (inFlight)
        return await inFlight;

    const guardedPromise = (async () => {
        try {
            return await operation();
        } finally {
            state.pendingMediaStarts.delete(operationKey);
        }
    })();

    state.pendingMediaStarts.set(operationKey, guardedPromise);
    return await guardedPromise;
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
    state.pendingMediaStarts.clear();
    state.pendingMediaToggles.clear();
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
                const started = await runMediaStartGuard('microphone', async () => {
                    stopProducer('micProducer');
                    stopStream('micStream', 'micTrack');

                    try {
                        state.micStream = await openMicrophoneStreamWithFallback();

                        const audioTracks = state.micStream.getAudioTracks();
                        if (!audioTracks || audioTracks.length === 0)
                            throw new Error('microphone stream has no audio track');

                        state.micTrack = audioTracks[0];
                        state.micTrack.contentHint = 'speech';
                        bindMicrophoneTrackLifecycle(state.micTrack);

                        const producer = await state.sendTransport.produce({
                            track: state.micTrack,
                            codecOptions: MICROPHONE_PRODUCER_CODEC_OPTIONS,
                            appData: {
                                serverProducerId: payload.serverProducerId,
                                trackType: 'microphone'
                            }
                        });

                        state.micProducer = producer;
                        wireLocalProducerLifecycle('micProducer', () => {
                            stopStream('micStream', 'micTrack');
                        });

                        return { producerId: producer.id };
                    } catch (error) {
                        stopProducer('micProducer');
                        stopStream('micStream', 'micTrack');
                        throw normalizeMediaStartError('microphone', error);
                    }
                });

                sendDiag('start_microphone.ok', {
                    producerId: started.producerId
                });
                sendResponse(requestId, true, started);
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
                const started = await runMediaStartGuard('camera', async () => {
                    stopProducer('cameraProducer');
                    stopStream('cameraStream', 'cameraTrack');
                    removeVideoTile(LOCAL_CAMERA_TILE_KEY);

                    try {
                        state.cameraStream = await openCameraStreamWithFallback();

                        const videoTracks = state.cameraStream.getVideoTracks();
                        if (!videoTracks || videoTracks.length === 0)
                            throw new Error('camera stream has no video track');

                        state.cameraTrack = videoTracks[0];
                        state.cameraTrack.contentHint = 'motion';
                        await applyTrackConstraintsSafe(state.cameraTrack, CAMERA_VIDEO_CONSTRAINTS);
                        bindCameraTrackLifecycle(state.cameraTrack);
                        await attachVideoTile(LOCAL_CAMERA_TILE_KEY, state.cameraStream, 'camera', true);

                        const producer = await produceVideoWithFallback(
                            state.cameraTrack,
                            payload,
                            'camera',
                            buildCameraProducerProfiles(),
                            'start_camera.produce');

                        state.cameraProducer = producer;
                        wireLocalProducerLifecycle('cameraProducer', () => {
                            stopStream('cameraStream', 'cameraTrack');
                            removeVideoTile(LOCAL_CAMERA_TILE_KEY);
                        });

                        return { producerId: producer.id };
                    } catch (error) {
                        stopProducer('cameraProducer');
                        stopStream('cameraStream', 'cameraTrack');
                        removeVideoTile(LOCAL_CAMERA_TILE_KEY);
                        throw normalizeMediaStartError('camera', error);
                    }
                });

                sendDiag('start_camera.ok', {
                    producerId: started.producerId
                });

                sendResponse(requestId, true, started);
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
                const started = await runMediaStartGuard('screen', async () => {
                    await resetLocalScreenCaptureState();

                    try {
                        state.screenStream = await openScreenStreamWithFallback();

                        const videoTracks = state.screenStream.getVideoTracks();
                        if (!videoTracks || videoTracks.length === 0)
                            throw new Error('screen stream has no video track');

                        state.screenTrack = videoTracks[0];
                        state.screenTrack.contentHint = 'detail';
                        await applyTrackConstraintsSafe(state.screenTrack, SCREEN_VIDEO_CONSTRAINTS);
                        bindScreenTrackLifecycle(state.screenTrack);
                        await attachVideoTile(LOCAL_SCREEN_TILE_KEY, state.screenStream, 'screen', true);

                        const producer = await produceVideoWithFallback(
                            state.screenTrack,
                            payload,
                            'screen',
                            buildScreenProducerProfiles(),
                            'start_screen.produce');

                        state.screenProducer = producer;
                        wireLocalProducerLifecycle('screenProducer', () => {
                            stopStream('screenStream', 'screenTrack');
                            removeVideoTile(LOCAL_SCREEN_TILE_KEY);
                        });

                        return { producerId: producer.id };
                    } catch (error) {
                        await resetLocalScreenCaptureState();
                        throw normalizeMediaStartError('screen', error);
                    }
                });

                sendDiag('start_screen.ok', {
                    producerId: started.producerId
                });

                sendResponse(requestId, true, started);
                return;
            }

            case 'stop_screen': {
                await resetLocalScreenCaptureState();
                sendResponse(requestId, true, {});
                return;
            }

            case 'set_microphone_enabled': {
                const enabled = !!payload.enabled;
                const responsePayload = await runMediaToggleGuard('microphone', async () => {
                    if (enabled) {
                        if (!state.micProducer || state.micProducer.closed)
                            throw new Error('microphone producer is not active');

                        await ensureMicrophoneTrackForProducer();
                        if (state.micTrack)
                            state.micTrack.enabled = true;

                        if (state.micProducer.paused)
                            await state.micProducer.resume();

                        return { enabled: true };
                    }

                    if (state.micTrack)
                        state.micTrack.enabled = false;

                    if (state.micProducer && !state.micProducer.closed && !state.micProducer.paused)
                        await state.micProducer.pause();

                    return { enabled: false };
                });

                sendResponse(requestId, true, responsePayload);
                return;
            }

            case 'set_camera_enabled': {
                const enabled = !!payload.enabled;
                const responsePayload = await runMediaToggleGuard('camera', async () => {
                    if (enabled) {
                        if (!state.cameraProducer || state.cameraProducer.closed)
                            throw new Error('camera producer is not active');

                        await ensureCameraTrackForProducer();
                        if (state.cameraTrack)
                            state.cameraTrack.enabled = true;

                        if (state.cameraProducer.paused)
                            await state.cameraProducer.resume();

                        return { enabled: true };
                    }

                    if (state.cameraTrack)
                        state.cameraTrack.enabled = false;

                    if (state.cameraProducer && !state.cameraProducer.closed && !state.cameraProducer.paused)
                        await state.cameraProducer.pause();

                    return { enabled: false };
                });

                sendResponse(requestId, true, responsePayload);
                return;
            }

            case 'set_screen_enabled': {
                const enabled = !!payload.enabled;
                const responsePayload = await runMediaToggleGuard('screen', async () => {
                    if (enabled) {
                        if (!state.screenProducer || state.screenProducer.closed)
                            throw new Error('screen producer is not active');

                        await ensureScreenTrackForProducer();
                        if (state.screenTrack)
                            state.screenTrack.enabled = true;

                        if (state.screenProducer.paused)
                            await state.screenProducer.resume();

                        return { enabled: true };
                    }

                    if (state.screenTrack)
                        state.screenTrack.enabled = false;

                    if (state.screenProducer && !state.screenProducer.closed && !state.screenProducer.paused)
                        await state.screenProducer.pause();

                    return { enabled: false };
                });

                sendResponse(requestId, true, responsePayload);
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
                    kind: 'audio',
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

                await playMediaElementSafe(audio);

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
                const normalizedTrackType = normalizeVideoTrackType(payload.trackType || 'camera');
                const consumeKind = resolveConsumeKind(payload.kind, normalizedTrackType);

                const consumer = await state.recvTransport.consume({
                    id: payload.consumerId,
                    producerId: payload.producerId,
                    kind: consumeKind,
                    rtpParameters: payload.rtpParameters,
                    appData: {
                        producerId: payload.producerId,
                        trackType: normalizedTrackType
                    }
                });

                const stream = new MediaStream();
                stream.addTrack(consumer.track);
                const trackType = normalizedTrackType;
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

if (typeof document !== 'undefined' && typeof document.addEventListener === 'function') {
    document.addEventListener('fullscreenchange', () => {
        if (!document.fullscreenElement && state.focusedTileKey && !state.videoTiles.has(state.focusedTileKey)) {
            state.focusedTileKey = null;
        }

        syncTileLayout();
    });
}

registerBridge();
window.__meetspaceForceHostReady = notifyHostReady;

if (document.readyState === 'complete') {
    setTimeout(notifyHostReady, 0);
} else {
    window.addEventListener('load', () => {
        setTimeout(notifyHostReady, 0);
    });
}