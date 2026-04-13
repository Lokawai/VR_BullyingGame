# Infrastructure - Threading (Main Thread Marshaling)

## What this is

Unity APIs generally must run on the **Unity main thread**, but networking and async workflows often run on background threads. The SDK has two mechanisms to safely marshal work back to the main thread:

1. **EventHub delivery** via `IUnityScheduler` (Domain contract, Runtime implementation)
2. **Ad‑hoc callbacks** via `MainThreadDispatcher` (Runtime utility)

This folder is documentation-only; the implementations live elsewhere.

## When to use what

### EventHub (`IUnityScheduler` → `UnityScheduler`)

If you're publishing/subscribing to domain events through `IEventHub`, prefer `EventDeliveryPolicy.MainThread` so handlers can safely touch Unity objects.

Relevant code:

- `../../Domain/EventSystem/IUnityScheduler.cs`
- `../../Runtime/Bootstrap/UnityScheduler.cs`

### MainThreadDispatcher (simple "run this on main thread")

If you have a callback on a background thread and you just need to run a Unity action:

```csharp
using Convai.Runtime.Threading;

MainThreadDispatcher.Post(() =>
{
    // Safe Unity API usage here
});
```

Relevant code:

- `../../Runtime/Threading/MainThreadDispatcher.cs`

Networking also defines an abstraction for dispatching:

- `../Networking/Abstractions/IMainThreadDispatcher.cs`
- `../Networking/Abstractions/MainThreadDispatcherAdapter.cs`

## Common pitfalls / gotchas

- `async/await` continuations can run on a background thread depending on the context. Don't assume you're on main thread.
- If you see "UnityException: get_* can only be called from the main thread", marshal back using the mechanisms above.
