# Convai SDK (Source + Architecture)

If you're integrating Convai into a Unity project (devs + designers), start with:

- `Documentation~/SETUP.md`
- `Documentation~/API-ENTRYPOINTS.md`

This `SDK/` folder is the **implementation** of the Convai Unity SDK. It's most useful when you need to:

- customize behavior/UI beyond the samples
- debug transport/session/audio issues
- contribute changes upstream

## Key runtime objects (what you add to scenes)

- `ConvaiManager` (`Runtime/Components/ConvaiManager.cs`) — primary scene entrypoint; ensures required Convai runtime components exist and are wired
- `ConvaiCharacter` (`Runtime/Components/ConvaiCharacter.cs`) — the NPC/agent you talk to
- `ConvaiPlayer` (`Runtime/Components/ConvaiPlayer.cs`) — local player identity + text input event
- `ConvaiAudioOutput` (`Runtime/Components/ConvaiAudioOutput.cs`) — recommended audio output companion (AudioSource config + optional 3D audio)
- `ConvaiRoomSession` (`Application/ConvaiRoomSession.cs`) — static session API for room-level events (connected/disconnected/participants)
- `ConvaiSDK` (`Application/ConvaiSDK.cs`) — static SDK metadata API (for example `Version`)

Internal runtime component (advanced):

- `ConvaiRoomManager` (`Runtime/Adapters/Networking/ConvaiRoomManager.cs`) — room/session lifecycle, mic, and transport (managed by `ConvaiManager`)

## Naming and ownership rules

- `Manager` is used for scene/runtime lifecycle MonoBehaviours and owned component coordinators.
- `Service` is used for DI/business orchestration.
- `Bridge` and `Adapter` are used for cross-layer translation and boundary wiring.
- `SDK` is reserved for static package identity/metadata surfaces (`Application/ConvaiSDK.cs`).
- Session/runtime hierarchical error codes are centralized in `Domain/Errors/SessionErrorCodes.cs`.

## Where configuration lives

- Project settings: `Edit > Project Settings > Convai SDK` (backed by `Runtime/Configuration/ConvaiSettings.cs`)
- Scene bootstrap helper: `GameObject > Convai > Setup Required Components` (adds `ConvaiManager`)

## Architecture in 30 seconds

The SDK is organized into layered folders to keep Unity-specific code separate from core logic:

- **Domain** — events, models, and interfaces; no Unity dependencies
- **Infrastructure** — networking/persistence/protocol implementations (LiveKit/WebRTC, etc.)
- **Application** — orchestration (room-level session API, services)
- **Runtime** — Unity MonoBehaviours, manager-driven bootstrap pipeline, adapters, and presentation/UI
- **Shared** — DI container + injectable interfaces
- **Modules** — optional features (Narrative, Vision, LipSync)

Rule of thumb: inner layers should not depend on outer layers.

## Start points by task

- Start/stop a conversation: `Runtime/Components/ConvaiCharacter.cs`
- Scene-level connect/disconnect + mic helpers: `Runtime/Components/ConvaiManager.cs`
- Session internals + transport/mic lifecycle: `Runtime/Adapters/Networking/ConvaiRoomManager.cs`
- Transcript UI routing/customization: `Runtime/Presentation/README.md`
- Vision publishing: `Modules/Vision/README.md` and `Runtime/Vision/IVisionFrameSource.cs`
- Lip sync module: `Modules/LipSync/README.md`
- Networking deep dive (LiveKit/WebRTC): `Infrastructure/Networking/README.md`

## Layer documentation

- [Domain](Domain/README.md)
- [Domain Event System](Domain/EventSystem/README.md)
- [Infrastructure](Infrastructure/README.md)
- [Application](Application/README.md)
- [Runtime](Runtime/README.md)
- [Shared](Shared/README.md)
- [Modules](Modules/README.md)
