# Convai Samples

These are **optional** scenes, prefabs, and scripts that demonstrate how to use the SDK. Samples are imported via Package Manager and can be removed without breaking the core SDK.

If you’re integrating Convai, start with:

- `Documentation~/SETUP.md`

## Which sample should I open?

| What you’re trying to do | Start here |
|---|---|
| Get a native “hello world” scene working | `Samples/BasicSample/Scenes/Basic Sample.unity` |
| Validate browser permission/connect flow (WebGL build) | `Samples/BasicSample/Scenes/Basic Sample.unity` |
| Hook transcripts into gameplay triggers/actions | `Behaviors/README.md` |

## Folder overview

- `Samples/BasicSample/` — minimal single-character demo + Scene Metadata example
- `Behaviors/` — sample `ConvaiCharacterBehaviorBase` / `ConvaiPlayerBehaviorBase` implementations
- `Samples/BasicSample/Art/` — sample assets used by the demos

For WebGL validation, build `Samples/BasicSample/Scenes/Basic Sample.unity` to WebGL and follow the browser gesture/HTTPS requirements in `Documentation~/PLATFORMS.md`.

> **Note:** Reusable UI prefabs (settings panel, transcript UI, notifications) are in the SDK package at `Packages/com.convai.convai-sdk-for-unity/Prefabs/`.

## For developers

Treat these as **reference implementations**. Copy what you need into your project, then customize.

Basic sample uses the shared `Convai.Sample.*` assemblies; LipSync sample is in `Convai.Samples.LipSyncSample`. The sample assemblies:

- references core SDK assemblies
- is **not** referenced by core SDK code (one-way dependency)
- can be removed without affecting the SDK runtime
