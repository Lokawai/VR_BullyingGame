# Domain Layer

## What this is

The Domain layer is the SDK's "source of truth" for:

- **domain events** (what happened)
- **value objects / models** (what data looks like)
- **ports/abstractions** that other layers implement

It has **no Unity dependencies** and is designed to stay portable.

## Who should read this

- Contributors adding new events/models/contracts
- Engineers debugging event flow across layers

Most Unity integrators won't need to touch Domain; start with:

- `Documentation~/SETUP.md`

## Why it exists

The SDK uses an event-driven architecture. Domain is where we define stable contracts so Runtime/Infrastructure can evolve without breaking each other.

## How it's used

- Infrastructure publishes events (e.g., "character ready", "transcript received")
- Runtime subscribes and reacts (UI, gameplay glue, state machines)

The pub/sub contract is `IEventHub`:

- `Domain/EventSystem/IEventHub.cs`

## When to add a new domain event

Add a domain event when:

- multiple parts of the SDK should react to a thing that happened
- you need a stable payload that can be consumed without a direct dependency

Keep events as **data**, not commands. Avoid Unity/external types.

## Go deeper

- Event hub details: `EventSystem/README.md`
- Architecture overview: `../README.md`
