# Modules (Optional Features)

## What this is

Modules are optional assemblies that add features on top of the core Convai Unity SDK. You can remove a module if you don't use it (after removing its components from scenes/prefabs).

If you're integrating the SDK, start with:

- `Documentation~/SETUP.md`

## Who should read this

- Integrators deciding whether they need **Narrative**, **Vision**, or **LipSync**
- Contributors adding a new optional feature without bloating core runtime

## Why modules exist

Some features are not "always on":

- Vision/video has privacy + bandwidth implications
- Narrative requires dashboard setup (sections/triggers) and is not needed for every project
- Lip sync is optional and character/presentation-specific

Shipping these as modules keeps the core SDK smaller and lets teams opt in.

## Available modules

### Narrative (`Convai.Modules.Narrative`)

Adds Unity components for Convai Narrative Design:

- `ConvaiNarrativeDesignManager` — sync sections and react to section changes
- `ConvaiNarrativeDesignTrigger` — fire backend triggers from gameplay events (collision/proximity/manual)

Docs:

- `Narrative/README.md`

### Vision (`Convai.Modules.Vision`)

Adds a publisher that streams visual context into the LiveKit room:

- `ConvaiVideoPublisher`

Docs:

- `Vision/README.md`

On native platforms, Vision publishes an `IVisionFrameSource`.
On WebGL, Vision publishes the visible Unity canvas.

### LipSync (`Convai.Modules.LipSync`)

Adds data-driven lip-sync playback, profile assets, mapping assets, and the runtime bridge from Convai lip-sync events to blendshape output:

- `ConvaiLipSyncComponent`

Docs:

- `LipSync/README.md`

## Removing a module safely

1. Remove the module's components from all scenes/prefabs (to avoid "Missing Script").
2. Delete the module folder under `SDK/Modules/`.
3. Let Unity recompile.

## Go deeper

- SDK architecture overview: `../README.md`
