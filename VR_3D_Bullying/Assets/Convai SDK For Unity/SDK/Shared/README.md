# Shared Layer

## What this is

The Shared layer holds cross-cutting code used by multiple layers:

- DI container + service locator
- "injectable" interfaces used by the manager-driven injection pipeline
- small types/extensions that shouldn't live in Domain or Runtime

If you're integrating the SDK, start with:

- `Documentation~/SETUP.md`

## Who should read this

- Contributors changing DI/registration patterns
- Integrators building custom injectable components (UI, notifications, settings panels, etc.)

## Why it exists

Shared exists "beside" the clean-architecture layers to avoid circular dependencies. Runtime and Infrastructure can both reference Shared without pulling each other in.

## How customization typically works

In this SDK, the standard flow is:

1. `ConvaiManager` initializes the runtime bootstrap pipeline
2. Internal `ConvaiServiceBootstrap` registers services in `ConvaiServiceLocator`
3. Internal `ConvaiCompositionRoot` discovers scene components and calls their `Inject(...)` methods

So for many customization points you:

- implement an `IInjectable*` interface (from `Interfaces/`)
- add the component to your scene/prefab
- let the manager pipeline inject it at startup

## Go deeper

- DI details: `DependencyInjection/README.md`
- Scene entrypoint: `../Runtime/Components/ConvaiManager.cs`
- Internal bootstrap: `../Runtime/Bootstrap/ConvaiServiceBootstrap.cs`
- Internal injection: `../Runtime/Bootstrap/ConvaiCompositionRoot.cs`
