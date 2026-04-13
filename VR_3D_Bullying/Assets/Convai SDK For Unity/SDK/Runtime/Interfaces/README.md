# Runtime Interfaces

## What this is

This folder contains Unity‑facing interfaces used by the Runtime layer.

At the moment, it primarily contains:

- `IInjectableComponent` — an **optional marker** that documents "this component supports DI".

## Important note about DI

`IInjectableComponent` is not required for injection.

The SDK's injection is driven by `ConvaiManager` through an internal composition pipeline that discovers known component types and calls their `Inject(...)` methods. The marker exists for clarity and future generic discovery.

## Go deeper

- Scene entrypoint: `../Components/ConvaiManager.cs`
- Internal injection/discovery: `../Bootstrap/ConvaiCompositionRoot.cs`
- DI container: `../../Shared/DependencyInjection/README.md`
