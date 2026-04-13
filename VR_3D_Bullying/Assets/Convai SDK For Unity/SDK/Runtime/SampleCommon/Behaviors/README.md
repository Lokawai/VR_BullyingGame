# Behaviors (Sample Pack)

This folder contains small, copy‑pasteable scripts that show how to react to Convai events (speech, transcripts, “ready”) in a modular way.

If you’re integrating the SDK, start with: `Documentation~/SETUP.md`.

## Who should read this

- **Designers / producers** who want to understand what can be driven by AI dialogue (animations, triggers, UI)
- **Engineers** who want a clean way to add “game rules” without forking the SDK

## Why behaviors exist

Convai scenes usually need small bits of glue code:

- “When the NPC starts speaking, play an animation”
- “When the NPC says a keyword, trigger a quest/shop UI”
- “When the backend says the character is ready, kick off a scripted moment”

The SDK supports this using an **interceptor chain**:

- Behaviors are ordered by **Priority** (higher runs first)
- For callbacks that return `bool`, return:
  - `true` to **consume/intercept** the event (stop the chain)
  - `false` to **observe** the event (let others run)

## How to use (Character behaviors)

1. On your NPC GameObject, add:
   - `Convai/Convai Character`
   - `Convai/Character Behavior Dispatcher`
2. Add one or more behavior components (implement `IConvaiCharacterBehavior`).
   - Recommended base class: `ConvaiCharacterBehaviorBase`
3. Set each behavior’s **Priority** field (higher runs earlier).

If you’re unsure this is wired correctly, open the sample scene:

- `Samples/BasicSample/Scenes/Basic Sample.unity` (after importing samples via Package Manager)

## What’s included

### Character behaviors (wired via `CharacterBehaviorDispatcher`)

- `SpeechAnimationBehavior`
  - Sets an Animator bool parameter named `IsSpeaking` on speech start/stop.
- `ShopkeeperBehavior`
  - Looks for commerce keywords in the **final** transcript and calls `agent.SendTrigger(...)`.
  - Because it returns `true` when it fires, it **consumes** that transcript event for lower-priority behaviors.
- `QuestGiverBehavior`
  - On `OnCharacterReady`, sends a `quest.step` trigger (example of a scripted “start” moment).

### Player behaviors / input handlers (reference only in this repo snapshot)

This repository contains base types (`ConvaiPlayerBehaviorBase`, `ConvaiPlayerInputHandlerBase`) and sample scripts:

- `PlayerPushToTalkBehavior`
- `PlayerSessionStateHandler`

…but there is no built-in dispatcher for these in this SDK snapshot (no equivalent of `CharacterBehaviorDispatcher`), so treat them as **examples/templates**.

If you want push‑to‑talk today, the simplest approach is usually to drive mic state via:

- `ConvaiManager.ToggleMicMute()` (or `ConvaiManager.Audio.ToggleMicMuted()`)

### Test doubles

The `Test*` scripts are used by edit-mode tests and are not intended for production.

## Common pitfalls / gotchas

- Behaviors must live on the **same GameObject** as the dispatcher and `ConvaiCharacter`.
- Start by returning `false` (observe) until you know you need to intercept.
- `agent.SendTrigger(...)` only helps if your Convai backend/project is set up to respond to that trigger name.
- Many rules should only run on **final** transcripts (interim results can change).

## Go deeper

- Setup + where to add components: `Documentation~/SETUP.md`
- Behavior system types:
  - `SDK/Runtime/Components/CharacterBehaviorDispatcher.cs`
  - `SDK/Runtime/Behaviors/Character/IConvaiCharacterBehavior.cs`
