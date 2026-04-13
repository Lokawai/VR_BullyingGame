# Application Layer

## What this is

The Application layer contains **Unity‑independent orchestration**: workflows that coordinate Domain concepts with Infrastructure services.

Most Unity integrators don't edit this layer directly; it exists so core workflows can be tested and evolved without Unity dependencies.

## Who should read this

- Contributors implementing a new workflow (transcript aggregation, vision coordination, narrative logic)
- Integrators who want to listen to **room‑level** events via `ConvaiRoomSession`
- Integrators who need SDK metadata (for example version) via `ConvaiSDK`

## Why it exists

This layer answers "how does the SDK coordinate multiple moving pieces?" without pulling in Unity types:

- domain events and state machines
- protocol/service orchestration
- formatting/filtering/aggregation (for UI)

## How it's used

### `ConvaiRoomSession` (public static session API)

`ConvaiRoomSession` is a static session API for **SDK‑wide state** and room‑level events (connected/disconnected/participants).

Important notes:

- Initialization is `ConvaiRoomSession.Initialize()` (no settings parameter).
- It is called automatically during manager bootstrap after core services are registered.
- For starting/stopping an NPC conversation, use `ConvaiCharacter.StartConversationAsync()` / `StopConversationAsync()`.

### `ConvaiSDK` (public static metadata API)

`ConvaiSDK` contains SDK-level metadata that is not tied to room/session runtime state.

```csharp
using Convai.Application;
using UnityEngine;

public class SdkInfoExample : MonoBehaviour
{
    private void Start()
    {
        Debug.Log($"Convai SDK Version: {ConvaiSDK.Version}");
    }
}
```

Typical usage:

```csharp
using Convai.Application;
using UnityEngine;

public class RoomEventsExample : MonoBehaviour
{
    private void OnEnable()
    {
        ConvaiRoomSession.OnRoomConnected += OnConnected;
        ConvaiRoomSession.OnRoomDisconnected += OnDisconnected;
    }

    private void OnDisable()
    {
        ConvaiRoomSession.OnRoomConnected -= OnConnected;
        ConvaiRoomSession.OnRoomDisconnected -= OnDisconnected;
    }

    private void OnConnected() => Debug.Log("Convai room connected");
    private void OnDisconnected() => Debug.Log("Convai room disconnected");
}
```

### Services in this folder

- `Services/Transcript/*` — aggregation/formatting/filtering for transcript presentation
- `Services/Vision/*` — vision coordination logic (consumed by Runtime/Modules)
- `Services/Narrative/NarrativeDesignController` — engine‑agnostic narrative logic used by the Narrative module

## Common pitfalls / gotchas

- This assembly is `noEngineReferences: true`: don't introduce Unity types here.
- Don't create long‑lived Unity objects from here; do that in Runtime and inject an abstraction instead.

## Go deeper

- SDK entrypoint and bootstrap: `Documentation~/SETUP.md`
- Runtime bootstrap + DI: `../Runtime/README.md`
- Domain layer (events/contracts): `../Domain/README.md`
