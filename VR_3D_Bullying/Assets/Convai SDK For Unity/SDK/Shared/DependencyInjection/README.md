# Shared - Dependency Injection (ServiceContainer + Service Locator)

## What this is

The SDK uses a small DI container (`ServiceContainer`) behind a static facade (`ConvaiServiceLocator`).

Most integrators don't interact with DI directly because the standard setup uses:

- `ConvaiManager` as the scene entrypoint
- internal `ConvaiServiceBootstrap` to register services
- internal `ConvaiCompositionRoot` to inject dependencies into scene components

Start with:

- `Documentation~/SETUP.md`

## Who should read this

- Contributors changing registration/injection behavior
- Integrators who need to replace a service (advanced)

## How the SDK uses it in Unity

At startup (triggered by `ConvaiManager`):

1. `ConvaiServiceLocator.Initialize()`
2. Internal `ConvaiServiceBootstrap` registers core services (EventHub, schedulers, UI controller, session services, etc.)

Then (internal `ConvaiCompositionRoot.Awake()`):

- discovers components in the scene and calls their `Inject(...)` methods

## Registering a custom service (advanced)

If you need to override a service, register it **before** the SDK registers its defaults (i.e., before manager bootstrap runs).

Example pattern:

```csharp
using Convai.Shared.DependencyInjection;

// Somewhere with execution order < -1100
ConvaiServiceLocator.Initialize();
ConvaiServiceLocator.Register(ServiceDescriptor.Singleton<IMyService>(c => new MyService()));
```

In most cases, prefer "injectable component" customization (drop a component in the scene and let the manager-driven injection pipeline inject it) over overriding global services.

## Resolving services

Prefer DI/injection. If you must resolve globally, use `TryGet` to avoid hard failures:

```csharp
if (ConvaiServiceLocator.TryGet(out IEventHub hub))
{
    // ...
}
```

## Go deeper

- Scene entrypoint: `../../Runtime/Components/ConvaiManager.cs`
- Internal bootstrap: `../../Runtime/Bootstrap/ConvaiServiceBootstrap.cs`
- Internal injection: `../../Runtime/Bootstrap/ConvaiCompositionRoot.cs`
