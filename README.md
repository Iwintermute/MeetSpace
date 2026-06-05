# MeetSpace Client
Клиентская часть MeetSpace (UWP + MAUI + shared core-библиотеки), отвечающая за:
- аутентификацию пользователя,
- realtime signaling/RPC обмен с backend,
- координацию чатов/конференций/звонков,
- локальное состояние сессии и медиа-сценарии.

## Что в репозитории
- `MeetSpace` — UWP-приложение (desktop shell).
- `MeetSpace.Mobile` — MAUI-приложение.
- `MeetSpace.Client.Application` — прикладной слой (coordinator-ы, feature clients, inbound routers).
- `MeetSpace.Client.Realtime` — WebSocket transport, gateway, RPC-клиент.
- `MeetSpace.Client.Bootstrap` — композиция зависимостей, runtime options.
- `MeetSpace.Client.Domain` — доменные сущности.
- `MeetSpace.Client.Contracts` — контракты протокола.
- `MeetSpace.Client.Shared` — общие модели, результаты, утилиты.
- `MeetSpace.Client.Infrastructure` — storage/paths.
- `MeetSpace.Client.Media` — media abstractions/services.
- `MeetSpace.Client.Security` — крипто-сервис и связанные абстракции.
- `MeetSpace.UI` — UI-компоненты.

## Как работает event bus (клиент)
В проекте используется логический event bus поверх realtime и внутрипроцессных событий:
1. UI/ViewModel вызывает coordinator или feature client.
2. Feature client отправляет RPC-команду через `IRealtimeRpcClient`.
3. RPC-клиент сериализует envelope и отправляет через `IRealtimeGateway`.
4. Gateway работает поверх `IRealtimeConnection` (WebSocket transport).
5. Ответы/события приходят обратно по цепочке Gateway -> RPC/InboundRouters.
6. InboundRouters обновляют Store (`SessionStore`, `ChatStore`, `CallStore`, `ConferenceStore`).
7. Store генерирует события изменения состояния для UI.

Итого: транспортный event stream + доменный event stream в сторах = единый event bus для приложения.

## Ключевые runtime-настройки
Конфигурация собирается через `ClientRuntimeOptions`:
- `MEETSPACE_REALTIME_ENDPOINT`
- `MEETSPACE_SIGNALING_ENDPOINT`
- `MEETSPACE_SUPABASE_URL`
- `MEETSPACE_SUPABASE_ANON_KEY`
- `MEETSPACE_MEDIA_AUTH_TOKEN`

## Полный набор документации
- Метод-уровневый reference клиента: `docs/client/CLIENT_METHOD_REFERENCE.md`
- Архитектура и event bus клиента: `docs/client/CLIENT_ARCHITECTURE_AND_EVENTBUS.md`
- Метод-уровневый reference сервера: `/diplom/docs/server/SERVER_METHOD_REFERENCE.md`
- Архитектура и event bus сервера: `/diplom/docs/server/SERVER_ARCHITECTURE_AND_EVENTBUS.md`
- Большой документ для ИИ-доклада и онбординга: `docs/PROJECT_REPORT_120P_AI_BRIEF.md`

## Для онбординга команды (8 человек)
Рекомендуемый порядок чтения:
1. Этот `README`.
2. `docs/client/CLIENT_ARCHITECTURE_AND_EVENTBUS.md`.
3. `docs/client/CLIENT_METHOD_REFERENCE.md` (по своим модулям).
4. Серверные docs в `/diplom/docs/server`.
5. `docs/PROJECT_REPORT_120P_AI_BRIEF.md` для общего архитектурного контекста, масштабирования и управленческой рамки.
