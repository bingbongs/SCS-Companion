# Changelog

## Unreleased — 2026-09-22

- Complete three persistent Custom shortcut banks with validation, MIDI Learn, and live control labels.
- Complete per-app media volume/mute, presentation shortcuts, streaming hotkeys, and vocal pitch buttons.
- Fix Windows keyboard input ABI used by media, productivity, Audacity, and Discord shortcuts.
- Move loop conversion, PCM copying, and overdub merging outside the mixer lock; eliminate the full native-capture copy and reserve bounded recording storage.
- Correct overdub timing after tempo changes, preserve manual tempo, interpolate fractional playback, and fix quantization rounding.
- Save immutable loop generations through an atomic manifest; validate restored sessions and await finalization at shutdown.
- Add live microphone metering, device-change routing refresh, reliable capture disposal, and paused inactive audio output.
- Resample Kaoss imports to 48 kHz, include both stereo channels, and avoid per-sample locks.
- Cancel macros on Safe Stop, disconnect, mode change, and exit; bound macro capture and fix Simon cancellation.
- Move diagnostic MIDI file writes off the UI thread and prevent pending MIDI discovery from reopening ports after shutdown.
- Remove unimplemented DJ and avatar-action banks from selectable modes; retain their future integration work in the roadmap.
- Add automated audio/control regression checks to Windows CI.

## 1.0.0 â€” 2026-07-16

- Standalone MIDI input and LED output for Stanton SCS.3d / DaScratch without DaRouter.
- Six reassignable hardware module slots with persistent settings.
- Mouse/trackball control and controller-action macro recording.
- Four-track synchronized microphone looper with overdub, quantization, routing, and effects.
- Native VRChat OSC controls for Desktop and PC VR.
- DJ utility mappings plus Mixxx and VirtualDJ profile installers.
- Windows media, productivity, Audacity, and Discord controls.
- Kaoss-style scale instrument and sample performer.
- Pink Trombone-inspired vocal-tract synthesizer.
- Infinite Simon memory game with controller lighting and persistent high score.
- Device-shaped WinUI interface, compact settings, themes, diagnostics, and safe output stop.
