# Infrastructure Layer

## What this is

Infrastructure is where the SDK talks to the outside world:

- LiveKit/WebRTC transport (connect, audio/video tracks, data messages)
- persistence (session IDs)
- protocol handling and external service adapters

If you're integrating into a Unity scene, start with:

- `Documentation~/SETUP.md`

## Who should read this

- Contributors working on transport/protocol/persistence
- Engineers debugging failures that originate outside gameplay code

## Why it exists

Infrastructure isolates third‑party dependencies and platform constraints so the rest of the SDK can stay testable and stable.

## Key subfolders

- Networking: `Networking/README.md`
- Persistence: `Persistence/README.md`
- Threading notes: `Threading/README.md`

## Common pitfalls / gotchas

- Network callbacks may arrive off the Unity main thread; use the SDK's scheduling utilities (see Threading README).
- WebGL has stricter browser security requirements (HTTPS + user gesture); see `Documentation~/PLATFORMS.md`.
