# Convai.Runtime.Behaviors (Extension Contracts)

## What this is

This folder contains the `Convai.Runtime.Behaviors` nested assembly: small, stable contracts that let you extend the SDK without depending on all runtime internals.

## Why it exists

Keeping behavior contracts in a small assembly helps:

- avoid circular dependencies (Modules can reference behaviors without pulling all Runtime)
- keep extension points stable for integrators

## What's actually wired in this SDK snapshot

### Character behaviors (supported)

The SDK includes a dispatcher component:

- `CharacterBehaviorDispatcher` (`../Components/CharacterBehaviorDispatcher.cs`)

It discovers `IConvaiCharacterBehavior` components on the same GameObject, sorts by `Priority` (higher first), and runs the interceptor chain.

Recommended base class:

- `ConvaiCharacterBehaviorBase`

Sample pack:

- `../SampleCommon/Behaviors/README.md`

### Player behaviors (contracts only)

This assembly also contains contracts/base classes for:

- `IConvaiPlayerBehavior` / `ConvaiPlayerBehaviorBase`
- `IConvaiPlayerInputHandler` / `ConvaiPlayerInputHandlerBase`

…but there is no built-in dispatcher for these in this snapshot. Treat them as **extension contracts** you can wire into your own systems if needed.

### Response handlers (Character)

- `IConvaiResponseHandler` (`Character/IConvaiResponseHandler.cs`)

An interceptor for generated Convai responses before they flow into the default transcript pipeline. Like character behaviors, handlers are sorted by `Priority` (higher first).

## Go deeper

- Character behavior interface: `Character/IConvaiCharacterBehavior.cs`
- Response handler interface: `Character/IConvaiResponseHandler.cs`
- Sample behaviors: `../SampleCommon/Behaviors/README.md`
