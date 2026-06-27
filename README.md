# MeetSpace Client

Cross-platform corporate communications client with real-time audio/video conferencing, direct calls, and encrypted messaging.

## Architecture

The solution is split into a **UWP shell** and a set of **.NET Standard 2.0 libraries**:

- **MeetSpace** — UWP app shell (XAML pages, view models, WebView2 media host, composition root)
- **MeetSpace.UI** — reusable UWP controls, animations, and materials
- **MeetSpace.Client.Domain** — domain models for calls, chat, conferences, and sessions
- **MeetSpace.Client.Application** — application services, coordinators, inbound routers, feature clients
- **MeetSpace.Client.Contracts** — transport contracts, protocol envelopes, actions, and message types
- **MeetSpace.Client.Realtime** — WebSocket connection, gateway, and RPC client
- **MeetSpace.Client.Security** — AES-256-CBC envelope encryption
- **MeetSpace.Client.Media** — media abstractions and services
- **MeetSpace.Client.Infrastructure** — local storage and path helpers
- **MeetSpace.Client.Shared** — base abstractions, result types, stores, config, JSON utilities
- **MeetSpace.Client.Bootstrap** — DI registration and runtime wiring

## Tech Stack

- .NET Standard 2.0, UWP (target 10.0.22621.0, min 10.0.18362.0)
- WebView2 + mediasoup-client (WebRTC media engine)
- Supabase (auth, REST-based sign-up/sign-in/refresh)
- WebSocket realtime transport with RPC correlation, auto-reconnect (exponential backoff + jitter)
- AES-256-CBC message encryption
- Microsoft.Extensions.DependencyInjection, System.Text.Json, Microsoft.Data.Sqlite

## Key Features

- **Conferencing & Direct Calls** — create, join, leave conferences; accept/decline direct calls; publish/consume audio, video, and screen-share tracks via mediasoup
- **Adaptive Quality** — server-side media stats + client-side QoS sampling; automatic degraded mode (camera pause) on poor network; auto-recovery
- **Fast Rejoin** — ICE failure detection triggers throttled transport/device re-bootstrap without full re-auth
- **Security** — WSS-only for remote endpoints, query-token WS auth, envelope encryption, certificate pinning

## License

Proprietary. All rights reserved.
