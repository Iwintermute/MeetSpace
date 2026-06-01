# MeetSpace Client Documentation
## 1. Обзор
Клиентская часть MeetSpace состоит из UWP shell (`MeetSpace`) и набора netstandard-библиотек (`MeetSpace.Client.*`), которые реализуют:
- авторизацию (Supabase),
- realtime signaling/RPC,
- call/chat/conference бизнес-логику,
- WebView2 media engine (mic/cam/screenshare),
- диагностику и адаптацию качества.

## 2. Структура решения и принадлежность модулей
## 2.1 UI shell
- `MeetSpace` (UWP app, `AppContainerExe`)
  - XAML UI, страницы, view models;
  - интеграция WebView2 media host;
  - composition root через `MeetSpaceHostBuilder`.
- `MeetSpace.UI`
  - переиспользуемые UWP controls, анимации, материалы, иконки.

## 2.2 Domain/application/shared
- `MeetSpace.Client.Domain`
  - доменные модели calls/chat/conference/session.
- `MeetSpace.Client.Application`
  - application services, coordinators, inbound routers, feature clients.
- `MeetSpace.Client.Shared`
  - базовые abstractions/results/stores/utilities/config/json helpers.
- `MeetSpace.Client.Contracts`
  - transport contracts + protocol envelopes/actions/message types.

## 2.3 Infrastructure/transport/security/media
- `MeetSpace.Client.Realtime`
  - WebSocket connection, gateway, RPC client.
- `MeetSpace.Client.Infrastructure`
  - local paths/storage helpers.
- `MeetSpace.Client.Security`
  - envelope encryption service (AES-256-CBC).
- `MeetSpace.Client.Media`
  - media abstractions/services.
- `MeetSpace.Client.Bootstrap`
  - DI registration и runtime wiring.

## 3. Основной технологический стек
Ядро:
- .NET Standard 2.0 библиотеки (`MeetSpace.Client.*`).
- UWP (`TargetPlatformVersion 10.0.22621.0`, Min 10.0.18362.0).
- WebView2 (через UWP `Microsoft.UI.Xaml.Controls.WebView2`).

NuGet/пакеты (ключевые):
- `System.Text.Json` (контракты/сериализация),
- `Microsoft.Extensions.DependencyInjection`,
- `Microsoft.Data.Sqlite`,
- `CommunityToolkit.*`,
- `Microsoft.UI.Xaml`,
- `TenMica`, `MicaForUWP`,
- `CompositionProToolkit`.

Web media host:
- `mediasoup-client` внутри `bridge-entry.js`,
- собранный bundle: `MeetSpace/WebView/MediaHost/bridge.bundle.js`.

## 4. Runtime-конфигурация клиента
`ClientRuntimeOptions` включает:
- `SupabaseUrl`, `SupabaseAnonKey`,
- `DefaultRealtimeEndpoint`,
- `DefaultDeviceId`,
- `MediaAuthToken` (опциональный токен для WS query auth),
- `IceServers` (опциональный список ICE/TURN/STUN),
- `ReconnectPolicy`,
- `CallRuntimeOptions` (timeouts, ABR/degraded пороги, интервалы sync).

Источник значений:
- default + env через `MeetSpaceHostBuilder` (например realtime endpoint, media auth token).

## 5. Авторизация и session lifecycle
## 5.1 Supabase auth
`SupabaseAuthClient`:
- sign up/sign in/refresh/logout через REST endpoints Supabase (`auth/v1/...`);
- хранение и валидация access/refresh token;
- извлечение `userId`, `email`, expiry.

## 5.2 Локальное хранение сессии
`App.xaml.cs`:
- сохраняет auth-сессию в `auth-session.json`;
- восстанавливает её на старте;
- делает refresh при необходимости;
- очищает state/file при невалидной сессии.

## 5.3 Realtime bind
`RealtimeSessionService`:
- поднимает WS подключение;
- выполняет `auth/session/bind_session` (RPC) с access token + deviceId;
- обновляет `SessionStore` (`selfPeerId`, state connection).

## 6. Realtime транспорт и RPC протокол
## 6.1 WebSocket слой
`ClientWebSocketConnection`:
- connect/disconnect/send/receive loop;
- keepalive;
- auto-reconnect (экспоненциальный backoff + jitter);
- callbacks/events: `Connected`, `Disconnected`, `Reconnecting`, `ReconnectFailed`.

Дополнительно:
- сертификатный bootstrap/pinning для `wss` (thumbprint-based validation callback).

## 6.2 Gateway слой
`RealtimeGateway`:
- сериализует `FeatureRequestEnvelope`,
- десериализует `FeatureResponseEnvelope`,
- прокидывает `EnvelopeReceived`.

## 6.3 RPC слой
`RealtimeRpcClient`:
- correlation по `requestId/clientRequestId/correlationId`,
- timeout/cancellation handling,
- pending map cleanup при disconnect.

Контракт:
- Request v2: `kind=request`, `request`, `object/agent/action/ctx`.
- Response: `dispatch_result` + payload/extensions.

## 7. Протоколы доменных фич
Основные object/action пространства:
- `conference/lifecycle`: create/get/join/leave/list members + media actions (`open_transport`, `publish_track`, `consume_track`, `media_stats`, ...).
- `direct_call/lifecycle`: create/accept/decline/hangup + media actions.
- `auth/session`: `bind_session`.

Message types (`ProtocolMessageTypes`):
- session/media lifecycle: `session_started`, `transport_opened`, `track_published`, ...
- conference/chat events,
- direct call lifecycle events.

## 8. Call/media архитектура (клиент)
## 8.1 Центральный coordinator
`CallCoordinator` управляет:
- join/leave session;
- open/connect transport;
- local publish (audio/video/screen);
- consume remote tracks;
- fast rejoin/recovery;
- adaptive quality + degraded mode.

Состояние:
- `CallStore` (`CallSessionState`, local media flags, participants, stage, degraded flag).

## 8.2 Feature clients
- `ConferenceMediaFeatureClient` — media RPC для конференций.
- `DirectCallFeatureClient` — media RPC для direct call.
- `MediasoupCallFeatureClient` — низкоуровневый mediasoup object client.

Они парсят:
- transport data (`ice/dtls/routerCaps/iceServers`),
- producers/consumers,
- media stats snapshot (включая loss/jitter/rtt/bitrate поля).

## 8.3 Inbound routers
- `CallInboundRouter` — применяет входящие signaling/media события в `CallStore`.
- `SessionInboundRouter` — connection state + peer assignment/bind response.

## 9. WebView2 media engine (UWP + JS bridge)
## 9.1 Host (C#)
`UwpWebViewAudioBridgeHost`:
- инициализирует WebView2;
- маппит virtual host на `WebView/MediaHost`;
- настраивает permissions (mic/cam allow);
- слушает сообщения bridge;
- включает runtime compatibility arg для screen capture;
- навигация в `index.html`.

## 9.2 Engine (C# adapter)
`WebViewAudioCallEngine`:
- реализует `IAudioCallEngine`;
- отправляет команды в bridge (`load_device`, `create_*_transport`, `start_*`, `consume_*`, ...);
- обрабатывает callbacks `transport_connect`, `transport_produce`;
- прокидывает telemetry events:
  - `CallQualityUpdated`,
  - `IceConnectionStateChanged`.

## 9.3 JS bridge
`bridge-entry.js` / `bridge.bundle.js`:
- `mediasoup-client.Device`;
- send/recv transports;
- lifecycle mic/cam/screen tracks с fallback constraints;
- remote consume rendering;
- QoS сбор через public `transport.getStats()`;
- ICE state через `connectionstatechange`;
- пост сообщений в host (`response`, `bridge_error`, `call_quality`, `ice_state`, ...).

## 9.4 UI media host page
`index.html`:
- CSP,
- grid/stage/strip layout,
- fullscreen/focus UX для video tiles.

## 10. QoS, ABR и degraded mode
Источники качества:
- server-side `media_stats` snapshot;
- bridge-side realtime QoS samples (`call_quality`).

`CallQualityTracker`:
- строит агрегированный quality report;
- score + label (`excellent/good/fair/poor/critical`).

`CallCoordinator`:
- периодическая adaptive check по `_adaptiveQualityCheckInterval`;
- автоматический вход в degraded mode (низкий bitrate/высокий packet loss);
- в degraded mode камера принудительно выключается/пауза producer;
- автоматический выход при восстановлении качества.

## 11. Reconnect и fast rejoin
Механизмы:
- websocket auto-reconnect policy (backoff/jitter/max attempts);
- `FastRejoinAsync` в coordinator для media path восстановления;
- ICE state trigger (`failed`/`disconnected`) -> throttled fast rejoin;
- bridge recovery path (`EnsureBridgeSessionReadyAsync`) с повторным bootstrap transport/device.

## 12. ICE/STUN/TURN и сетевой контур
Поток:
1) server возвращает `iceServers` в transport/capabilities response;
2) feature client парсит и кладёт в `WebRtcTransportInfo`;
3) `WebViewAudioCallEngine` передаёт `iceServers` в JS bridge;
4) bridge создаёт transports с этими ICE servers.

Дополнительно:
- runtime options также поддерживают preconfigured `IceServers`.

## 13. Безопасность (клиент)
## 13.1 Endpoint policy
`RealtimeSessionService.ValidateRealtimeEndpoint`:
- требует `wss://` для remote endpoint;
- `ws://` разрешён только для localhost loopback.

## 13.2 WS auth query token
При наличии `MediaAuthToken`:
- автоматически добавляется в query параметр `auth` при connect.

## 13.3 Auth/session protection
- bind_session выполняется только после валидной auth session;
- токены нормализуются и проверяются при restore/refresh.

## 13.4 Crypto service
`Aes256EnvelopeEncryptionService`:
- AES-256-CBC,
- random IV per message,
- base64 envelope.

## 13.5 WebView hardening
- CSP в `index.html`;
- host-side permission management;
- controlled message bridge between JS и C#.

## 14. Сборка, проверка и ограничения
Что подтверждено:
- `MeetSpace.Client.App.csproj` успешно собирается (warnings без errors).
- `server.js` синтаксически валиден (`node --check`).
- `bridge.bundle.js` пересобран из `bridge-entry.js`.

Известный внешний blocker:
- UWP host проект `MeetSpace.csproj` падает с `MSB4019` из-за отсутствия `Microsoft.Windows.UI.Xaml.CSharp.targets` в текущем toolchain окружении (не кодовая ошибка приложения).

## 15. Краткий end-to-end media сценарий
1) Пользователь аутентифицируется (Supabase), client bind’ит realtime session.
2) `CallCoordinator` открывает send/recv transport через direct/conference feature client.
3) `IAudioCallEngine` (WebViewAudioCallEngine) загружает mediasoup device и создаёт transports в bridge.
4) Публикация local tracks (mic/cam/screen), consume remote tracks.
5) Фоновая синхронизация producers/consumers + QoS сбор.
6) При деградации — degraded mode; при обрыве ICE — fast rejoin.
7) При завершении звонка — close session + cleanup local/remote media state.
