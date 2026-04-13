# Infrastructure - Networking (LiveKit WebRTC)

## What this is

This layer contains the SDK's real‑time transport implementation. It connects to Convai's backend, joins a LiveKit room, publishes local audio (and optional video), and routes data messages back into the SDK as domain/runtime events.

If you're integrating into a Unity scene, start with:

- `Documentation~/SETUP.md`

## Who should read this

- Contributors changing transport behavior (connect, reconnect, audio/video tracks, data messages)
- Integrators debugging connection/mic/audio issues beyond basic setup

## Why it exists

Networking depends on third‑party SDKs (LiveKit) and platform constraints (WebGL vs native). Keeping that in Infrastructure:

- isolates platform-specific code
- keeps Runtime components smaller
- makes it easier to swap or wrap transport details behind interfaces

## End-to-end flow (high level)

1. **Runtime** (`ConvaiCharacter` / `ConvaiManager`) initiates a connect with:
   - API key + server URL (`ConvaiSettings`)
   - connection type (audio vs video)
   - character id (+ optional session info)
   - internally this is executed by `ConvaiRoomManager`
2. **Infrastructure** requests room details/token via the REST API and joins the LiveKit room.
3. **Audio** publishes the microphone track and subscribes to remote audio tracks.
4. **Data messages** (RTVI/protocol) are parsed and turned into domain events / runtime callbacks.
5. Optional **video** is published by the Vision module (`ConvaiVideoPublisher`) when connection type is video.

## Platform split (Native vs WebGL)

Some pieces differ per platform (notably HTTP transport, LiveKit bindings, browser gesture policy, and video publishing shape). This repo keeps platform-specific implementations under:

- `Native/`
- `WebGL/`

WebGL support is shipped as an optional module and may not be present in all SDK distributions. When present, the active implementation is selected through factories registered by the runtime bootstrap pipeline.

Current WebGL specifics:

- room connect and character-details lookup use coroutine-backed `UnityWebRequest` paths
- browser audio start still requires a user gesture
- in-game vision publishes the visible Unity canvas instead of a Unity `RenderTexture`

## Debugging checklist

When "it connects but nothing happens":

- Confirm the basics: API key + Character ID set (`Documentation~/TROUBLESHOOTING.md`)
- Check mic permission and device availability (mobile especially)
- For WebGL (module only): HTTPS + user gesture requirements can block audio start (`Documentation~/PLATFORMS.md`)
- Turn logging up to `Info` in `Edit > Project Settings > Convai SDK`

## Go deeper

- Runtime entrypoint: `../../Runtime/Components/ConvaiManager.cs`
- Runtime transport/session internals: `../../Runtime/Adapters/Networking/ConvaiRoomManager.cs`
- Runtime connection lifecycle: `../../Runtime/Adapters/Networking/ConvaiRoomManager.cs`
- Room controllers: `Native/NativeRoomController.cs` and `WebGL/WebGLRoomController.cs`
