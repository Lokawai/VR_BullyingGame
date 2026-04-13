# Infrastructure - Persistence (Session IDs)

## What this is

This folder contains persistence utilities used by the SDK to store **session identifiers** across runs.

It does **not** store transcripts, audio, or conversation history. It only persists the IDs needed to attempt a reconnect/resume.

## Who should read this

- Contributors working on reconnect/session logic
- Integrators who need a custom persistence backend (file, secure store, cloud, etc.)

## Why it exists

Some transports can reconnect to an existing room/session if the client provides the right identifiers. Persisting these IDs makes it possible to:

- resume a character session after app restart (when supported)
- rejoin after transient disconnects

## What's in here

- `KeyValueStoreSessionPersistence` — the default implementation (stores per-character session IDs using an `IKeyValueStore`)

The core contracts live in Domain:

- `ISessionPersistence`: `../../Domain/Abstractions/ISessionPersistence.cs`
- `IKeyValueStore`: `../../Domain/Abstractions/IKeyValueStore.cs`

Unity's default key-value store implementation lives in Runtime:

- `PlayerPrefsKeyValueStore`: `../../Runtime/Persistence/PlayerPrefsKeyValueStore.cs`

## How it's used

During manager bootstrap, the internal `ConvaiServiceBootstrap` creates `ISessionService` and will use a custom `ISessionPersistence` if one is registered; otherwise it falls back to:

- `new KeyValueStoreSessionPersistence(new PlayerPrefsKeyValueStore())`

## Common pitfalls / gotchas

- Session resume is configured **per character** on `ConvaiCharacter.EnableSessionResume`.
- Different characters in the same scene can use different session resume behavior.
- Clearing PlayerPrefs (or changing key prefixes) will invalidate stored sessions.

## Go deeper

- Scene entrypoint: `../../Runtime/Components/ConvaiManager.cs`
- Internal bootstrap registration: `../../Runtime/Bootstrap/ConvaiServiceBootstrap.cs`
- Reconnect flow: `../../Runtime/Adapters/Networking/ConvaiRoomManager.cs`
