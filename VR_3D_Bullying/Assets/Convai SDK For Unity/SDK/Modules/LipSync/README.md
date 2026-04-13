# Convai LipSync V2 (Data-Driven, Profile-ID Based)

## Core Model
- Lip sync profile is represented by `LipSyncProfileId` (string-based stable ID), not enum.
- Profile metadata is defined by `ConvaiLipSyncProfileAsset`:
  - `profileId`
  - `transportFormat`
- Profile sets are grouped with `ConvaiLipSyncProfileRegistryAsset`.
- Runtime catalog is `LipSyncProfileCatalog`.
- Source blendshape schema is defined in `ConvaiLipSyncMapAsset` (single source of truth).

## Built-In Profiles
SDK ships built-in profile assets and built-in registry under:
- `Resources/LipSync/Profiles`
- `Resources/LipSync/ProfileRegistries/LipSyncBuiltInProfileRegistry.asset`

Built-in IDs:
- `arkit`
- `metahuman`
- `cc4_extended`

## Extension Registry Workflow
To add custom profiles without code changes:
1. Create `ConvaiLipSyncProfileAsset` assets.
2. Create `ConvaiLipSyncProfileRegistryAsset`.
3. Put the registry under `Resources/LipSync/ProfileRegistries`.

Merge order is deterministic:
1. Built-in registry first.
2. Extension registries next, ordered by `priority ASC`, then `asset path ASC`.
3. Duplicate `profileId` uses last-wins and emits warning.

## Runtime Transport Contract
`LipSyncTransportOptions` carries full contract:
- `ProfileId`
- `Format`
- `SourceBlendshapeNames`
- chunk/fps/provider settings

Room-level resolution (`ConvaiRoomManager`):
- no valid source => disabled
- one unique contract => enabled
- multiple different contracts => error + `SessionError(lipsync.contract_conflict)` + disabled

## Strict Parser Policy
`LipSyncServerMessageParser` is deterministic and fail-closed:
- invalid transport options => drop
- payload format mismatch => drop (`lipsync.parse.format_mismatch`)
- numeric frame count mismatch => tolerated (extra values ignored, missing values padded with `0`)
- no profile detection/fallback logic
- optional sequence metadata parsing:
  - `sequence` / `seq` / `sequence_number`

`LipSyncPackedChunk.ProfileId` is always set from expected room transport options, not inferred from payload.

## Runtime Modes
- `Packed`: production playback path.
- `PackedShadow`: packed playback remains authoritative, while a parallel shadow comparator can be used for divergence analysis.

## Clock Source Behavior
- Clock selection is fixed when the runtime controller initializes.
- Non-WebGL platforms use `DspTimePlaybackClock` for audio-hardware-locked timing.
- WebGL uses `RealtimePlaybackClock` because browser DSP-time semantics differ from native platforms.
- Clock ownership stays inside the lip sync module; `PlaybackClockCoordinator` does not swap clocks after initialization.

## Component + Mapping
- `ConvaiLipSyncComponent` uses `_lockedProfileId` (string profile id).
- Profile lookup is done through `LipSyncProfileCatalog`.
- Unknown profile => component disables itself (fail-closed).
- Character binding uses `ICharacterIdentitySource` on the same GameObject.
- `ConvaiLipSyncMapAsset` targets `_targetProfileId`.
- Runtime source schema for transport/parsing is derived from the effective map's source channels.
- Default map registry is list-based: `List<ProfileDefaultMapEntry>`.
- Runtime playback model is packed-only (`LipSyncPackedChunk`).
- On WebGL, playback can also begin from the room-audio activation state once browser audio is active, even if a native-style per-character playback-start event is not emitted.

## Validation Suite
- `LipSyncServerMessageParserTests`: contract + drop-reason correctness.
- `LipSyncParserFuzzTests`: fuzz/chaos invalid payload resilience.
- `LipSyncFrameRingBufferStressTests`: bounded-buffer long-run guarantees.
- `LipSyncShadowRuntimeComparatorTests`: shadow compare correctness.
- `LipSyncPerformanceBenchmarks`: parser latency and false-drop acceptance checks.
- Acceptance report template: `SDK/Modules/LipSync/Docs/LipSyncAcceptanceReport.md`.

## Editor
Dynamic profile UX:
- `ConvaiLipSyncComponentEditor`
- `ConvaiLipSyncMapAssetEditor`
- `ConvaiLipSyncMapDebugWindow`
- `ConvaiLipSyncProfileAssetEditor`

Editor validation surfaces:
- unknown profile id
- duplicate profile id / catalog issues
- missing default map
- invalid profile transport config

Source schema workflow:
1. Open or create `ConvaiLipSyncMapAsset`.
2. Set `Target Profile` to the desired profile id.
3. Define source channels in map entries (or auto-detect/import via map editor tools).
4. Assign map to `ConvaiLipSyncComponent` or register it as profile default.
5. Runtime transport uses map source channels as the expected schema.

## Mapping Import Format
`ConvaiLipSyncMapAssetEditor` supports importing mappings from:
- JSON (recommended)
- tuple-style text payloads (for external tools/pipelines)
- shorthand pairs (source + target only)

Recommended JSON schema:
```json
{
  "targetProfileId": "cc4_extended",
  "description": "Imported from external pipeline",
  "globalMultiplier": 1.0,
  "globalOffset": 0.0,
  "allowUnmappedPassthrough": true,
  "mappings": [
    {
      "sourceBlendshape": "JawOpen",
      "targetNames": ["Jaw_Open"],
      "enabled": true,
      "multiplier": 1.0,
      "offset": 0.0,
      "useOverrideValue": false,
      "overrideValue": 0.0,
      "ignoreGlobalModifiers": false,
      "clampMinValue": 0.0,
      "clampMaxValue": 1.0
    }
  ]
}
```

Editor also provides `Copy JSON` to export the current map as this schema.

Shorthand pair examples:
```txt
EyeBlinkLeft -> Eye_Blink_L
EyeLookDownLeft: Eye_L_Look_Down
MouthFunnel = Mouth_Funnel_Down_R | Mouth_Funnel_Down_L | Mouth_Funnel_Up_R | Mouth_Funnel_Up_L
```

When only source/target is provided, all optional numeric/boolean fields use default values.
