# Vision Module (Video Streaming)

## What this is

The Vision module publishes a **video track** to the Convai room so characters can receive visual context from your Unity scene.

This module is optional because video has real privacy/performance implications.

On native platforms, the publisher streams a Unity `RenderTexture` produced by an `IVisionFrameSource`.
On WebGL, the publisher streams the visible Unity browser canvas via `canvas.captureStream()`.

## Who should read this

- Integrators enabling **Video** mode (via the manager-driven room pipeline)
- Engineers building a custom vision source (screen capture, AR passthrough, etc.)

## Why it exists

The core SDK supports audio + transcripts. Vision is shipped as a module so projects can:

- opt in only when needed
- choose *what* the AI should see (camera, webcam, render texture)
- control bandwidth and capture costs

## How it's used (recommended path)

1. Ensure the module is present in your project (this folder exists and compiles as `Convai.Modules.Vision`).
2. In your scene, select the object with `ConvaiManager` (usually `Systems`) and set the managed `ConvaiRoomManager`:
   - **Connection Type** → `Video`
3. In the Unity Editor, if required vision components are missing, the managed room pipeline will prompt you to auto‑add:
   - `ConvaiVideoPublisher` (publishes to LiveKit)
   - `CameraVisionFrameSource` (captures from a Unity Camera)

   These are created under a child GameObject named `ConvaiVisionRoot`.
4. Press Play and connect. When the room connects, the publisher starts capture and publishes the track.

Optional (Editor only): add a preview overlay so you can verify what is being streamed:

- `Convai/Vision/Vision Debug Preview (Editor Only)` (`VisionDebugPreview`)

## Choosing a frame source

On native platforms, the publisher works with any `IVisionFrameSource`.

Built-in options:

- `CameraVisionFrameSource` (Runtime) — captures from a Unity `Camera`
  - Inspector values of `0` fall back to `ConvaiSettings.VisionCaptureWidth/Height/FrameRate`
- `WebcamVisionFrameSource` (Samples) — captures from a physical webcam device

Custom sources:

- Implement `Convai.Runtime.Vision.IVisionFrameSource`
- Provide a `RenderTexture` via `CurrentRenderTexture`

Important: `IVisionFrameSource.CurrentRenderTexture` must be **top-down (Y-flipped)** for correct streaming.

### WebGL scene publishing

- WebGL does not publish Unity `RenderTexture` or camera sources directly.
- `ConvaiVideoPublisher` captures the visible Unity canvas in the browser and publishes that video track instead.
- Assigned `IVisionFrameSource` components are ignored on WebGL.
- This path is intended for in-game scene video, not browser screen-share or device camera capture.

## Common pitfalls / gotchas

- Vision is enabled by setting **Connection Type** to `Video` on the manager-managed `ConvaiRoomManager`.
  - `ConvaiSettings.VisionEnabled` exists, but it does not currently gate the video pipeline in this snapshot.
- `ConvaiSettings.VisionJpegQuality` exists, but this pipeline streams **video** (not JPEG frames), so it is currently unused.
- WebGL audio playback still requires a user gesture on most browsers. The default SDK flow consumes the first non-UI scene click after room connection and calls `EnableAudioAndStartListening()`. Use an explicit button in UI-heavy scenes.
- WebGL/mobile camera & mic permissions are product‑level UX requirements; see `Documentation~/PLATFORMS.md`.
- Start conservative: low resolution + ~10–15fps to reduce bandwidth and CPU/GPU load.
- `VisionDebugPreview` is tied to `IVisionFrameSource.CurrentRenderTexture` and does not preview the WebGL canvas-capture path in this snapshot.

## Go deeper

- Platform constraints: `Documentation~/PLATFORMS.md`
- Project settings reference: `Documentation~/PROJECT-SETTINGS.md`
- Publisher: `ConvaiVideoPublisher.cs`
- Frame source interface: `../../Runtime/Vision/IVisionFrameSource.cs`
- Camera capture source: `../../Runtime/Vision/CameraVisionFrameSource.cs`
- Debug preview: `../../Runtime/Vision/VisionDebugPreview.cs`
