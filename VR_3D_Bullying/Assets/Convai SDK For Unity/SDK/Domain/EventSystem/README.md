# Event System (Domain)

## What this is

This folder (`Domain/EventSystem/`) contains the **event bus infrastructure** (`IEventHub`, `EventHub`, subscription types, delivery policies).

The actual event payloads live under:

- `Domain/DomainEvents/`

## Who should read this

- Contributors wiring new event flows across layers
- Engineers debugging "who publishes what, and when?"

## Why it exists

The SDK uses events to keep layers decoupled:

- Infrastructure can publish "what happened" without knowing about UI/gameplay
- Runtime can subscribe and react without tight coupling to transport/protocol code

## How to use it

Subscribe:

```csharp
using Convai.Domain.EventSystem;
using Convai.Domain.DomainEvents.Session;

SubscriptionToken token = eventHub.Subscribe<SessionStateChanged>(
    e => UnityEngine.Debug.Log($"State: {e.NewState}"),
    deliveryPolicy: EventDeliveryPolicy.MainThread);
```

Publish:

```csharp
eventHub.Publish(SessionStateChanged.Create(oldState, newState, sessionId: null));
```

Unsubscribe when you're done:

```csharp
eventHub.Unsubscribe(token);
```

## When to use EventHub vs. component events

Use **EventHub** when:

- multiple systems need the same signal (UI + analytics + gameplay)
- the publisher shouldn't depend on the consumer (cross-layer)

Use **component events** when:

- you're wiring gameplay/UI in a scene and want direct ownership (e.g., `ConvaiCharacter.OnTranscriptReceived`)
- you're building a UI overlay via `ITranscriptListener` / `ITranscriptUI`

## Common pitfalls / gotchas

- Choose the right `EventDeliveryPolicy` (Unity work should run on main thread).
- Don't forget to unsubscribe if you hold strong references elsewhere.
- Keep event handlers lightweight; move heavy work off the main thread.

## Go deeper

- Event hub contract: `IEventHub.cs`
