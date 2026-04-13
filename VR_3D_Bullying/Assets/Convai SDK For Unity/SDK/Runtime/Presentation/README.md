# Runtime - Presentation (Transcript UI)

## What this is

This folder contains the SDK's transcript UI system:

- listens for transcript/runtime events
- aggregates messages using a strategy (Chat / Subtitle / Q&A)
- routes output to one or more registered UIs
- optionally fans out raw text to simple listeners

## Who should read this

- Integrators building custom UI (HUD subtitles, chat bubbles, logs)
- Contributors changing how transcript presentation works

For API selection between `ConvaiManager.Events`, `ITranscriptListener`, and `ITranscriptUI`, see `Documentation~/API-ENTRYPOINTS.md`.

## Why it exists

Transcript UI needs to balance:

- "simple access" (just give me the text)
- "product UI" (chat history, subtitle mode, Q&A formatting)
- runtime switching between UI styles

This layer keeps that logic out of `ConvaiCharacter` and out of networking code.

## How to customize (recommended path)

### Option A: implement `ITranscriptListener` (fastest)

If you just want the text so you can drive your own UI, implement:

- `Convai.Runtime.Presentation.Services.ITranscriptListener`

`ConvaiManager`'s internal bootstrap/injection pipeline auto-discovers and registers listeners at startup.

### Option B: implement `ITranscriptUI` (full control)

If you want the controller to manage activation + strategy output, implement:

- `Convai.Runtime.Presentation.Services.ITranscriptUI`

Set `Identifier` to one of:

- `"Chat"`
- `"Subtitle"`
- `"QuestionAnswer"`

`ConvaiManager`'s internal bootstrap/injection pipeline registers all `ITranscriptUI` instances it finds in the scene.

## Switching UI mode (Runtime Settings)

Mapping (0-based):

- `0` → Chat
- `1` → Subtitle
- `2` → Question/Answer

At runtime, mode/enablement is wired through:

- `IConvaiRuntimeSettingsService`
- `RuntimeSettingsTranscriptApplier` → `TranscriptUIController.SetModeByIndex(...)`

## Common pitfalls / gotchas

- Missing `ConvaiManager` means transcript services will not be registered/injected.
- If your UI starts inactive, keep manager-driven discovery/injection configured to include inactive objects (default behavior).
- Duplicate `Identifier` values will replace earlier registrations.
- If transcript UI is disabled in runtime settings (`TranscriptEnabled`), the controller will hide/clear UIs.

## Go deeper

- Controller: `Services/TranscriptUIController.cs`
- UI contract + enum: `Services/ITranscriptUI.cs`
- Listener contract: `Services/Core/ITranscriptListener.cs`
