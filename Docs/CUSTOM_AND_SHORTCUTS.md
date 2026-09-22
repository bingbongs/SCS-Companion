# Custom mappings and completed shortcut banks

## Custom

Assign Custom to a hardware slot in Settings → Modules. Its mode button cycles Profile 1, 2, and 3. In Settings → Custom, choose a profile and control, enter a shortcut, and select Save mapping. Learn control selects B11–B14 or PLAY/CUE/SYNC/TAP from the next physical press without executing that press. A blank shortcut clears the mapping.

Examples: `Ctrl+Shift+K`, `Alt+F13`, `PageDown`, `PlayPause`, `VolumeUp`. Optional modifiers are Ctrl, Alt, Shift, and Win. Supported keys include letters, digits, F1–F24, arrows, navigation keys, and common media keys. Each press sends a complete chord to the focused application; repeated note-on messages are suppressed until release. The three banks persist in `%LOCALAPPDATA%\SCSCompanion\custom-mappings.json`.

## Streaming

The Productivity → Streaming bank sends Ctrl+Alt+F13 through Ctrl+Alt+F20. B11–B14 use F13–F16; PLAY/CUE/SYNC/TAP use F17–F20. Assign those chords in OBS Settings → Hotkeys (or your streaming application's equivalent). Actions are intentionally user-assigned; the companion does not start a broadcast by default.

## Presentation

Mouse → Presentation sends F5 to start, Escape to exit, PageUp/PageDown on SYNC/TAP, left/right arrows on B11/B12, and B/W on B13/B14 for presentation applications that support black/white screens. Normal pointer and macro controls remain in Trackpad and Trackball.

## Per-app media mixer

Media → Per-app mixer selects the previous/next Windows audio session using B11/B12. GAIN sets the selected session's volume. B13/B14 adjust it by five percentage points; PLAY/TAP toggle mute. The status message names the application. Sessions are refreshed during interaction and follow the Windows multimedia output. CUE/SYNC show the current selection without changing it.

## Pink Trombone

B13/B14 lower/raise pitch by one semitone, within 70–320 Hz. GAIN now reaches silence at its minimum. Leaving the module releases Hold.

## Integration boundaries

DJ exposes the implemented Serato, Mixxx, and VirtualDJ MIDI utility banks. Traktor, rekordbox, djay, automatic DJ detection, and avatar-specific VRChat actions require additional integrations and are no longer selectable no-op banks. Native VRChat Auto/Desktop/PC VR controls remain available.
