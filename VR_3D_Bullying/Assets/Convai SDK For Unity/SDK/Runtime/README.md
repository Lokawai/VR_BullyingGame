# Runtime Layer (Unity Integration)

## What this is

Runtime is the Unity-facing layer: MonoBehaviours, scene setup, injection glue, and UI components. This is what most integrators interact with.

Start here if you're integrating:

- `Documentation~/SETUP.md`
- `Documentation~/API-ENTRYPOINTS.md`

## The "what do I add to my scene?" answer

Required scene objects (created by the setup wizard):

- `ConvaiManager` (scene entrypoint; execution order -1100)

`ConvaiManager` manages the runtime bootstrap pipeline internally and ensures the required runtime components are present (`ConvaiServiceBootstrap`, `ConvaiRoomManager`, `ConvaiCompositionRoot`).

Per-character/per-player:

- `ConvaiCharacter` on each NPC/agent
- `ConvaiPlayer` on the player
- Optional but recommended: `ConvaiAudioOutput` on the character (configures `AudioSource`)

## Why it exists

The inner layers (Domain/Application/Infrastructure) are pure C# and don't depend on Unity. Runtime is where we:

- adapt those services to Unity lifecycle
- expose stable components that designers can place in scenes
- keep gameplay/UI wiring straightforward

## Extension points you'll actually use

- **Gameplay hooks on NPCs:** `CharacterBehaviorDispatcher` + `IConvaiCharacterBehavior`
  - See `SampleCommon/Behaviors/README.md`
- **Custom transcript UI:** `ITranscriptListener` (simple) or `ITranscriptUI` (advanced)
  - See `Presentation/README.md`
- **Vision sources (if using Video mode):** implement `IVisionFrameSource`
  - See `../Modules/Vision/README.md`

## Common pitfalls / gotchas

- Missing `ConvaiManager` will cause setup/injection failures.
- Multiple `ConvaiPlayer` components: only the first discovered player is used.
- WebGL/mobile require permission UX (mic/camera); see `Documentation~/PLATFORMS.md`.

## Go deeper

- Scene manager entrypoint: `Components/ConvaiManager.cs`
- Internal bootstrap registration: `Bootstrap/ConvaiServiceBootstrap.cs`
- Internal injection/discovery: `Bootstrap/ConvaiCompositionRoot.cs`
