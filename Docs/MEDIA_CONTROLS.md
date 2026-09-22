# Windows media controls

The Media vertical slice is active in `EQ` → `Media` → `System`, `Playback`, and `Microphone`. The footer reads `MEDIA` while any live bank is selected.

| SCS.3d control | Windows action |
| --- | --- |
| GAIN strip | Absolute default-output volume, 0–100% |
| PLAY | Global play/pause |
| CUE | Previous track |
| SYNC | Next track |
| TAP | Mute/unmute default output |
| B11 | Volume down |
| B12 | Volume up |
| B13 | Stop playback |
| B14 | Open the default media application |

The transport buttons use Windows media keys, so they follow the operating system's active media session and work with common music, video, and browser players. The GAIN strip talks directly to the current default Windows audio endpoint and follows default-device changes on the next adjustment.

## Microphone bank

| SCS.3d control | Default Windows microphone action |
| --- | --- |
| GAIN strip | Absolute input level, 0–100% |
| B11 / B12 | Input level down/up by 5% |
| B13 | Toggle microphone mute |
| TAP | Toggle microphone mute |
| B14 | Explicitly unmute microphone |

B13 remains brightly lit in both the app and hardware while the default microphone is muted. The endpoint is refreshed periodically, so changing the default Windows microphone is picked up without restarting the companion.

`Per-app mixer` uses B11/B12 to select the previous/next Windows audio session, GAIN for its absolute volume, B13/B14 for 5% adjustments, and PLAY/TAP for mute. The status message identifies the selected application. Start audio playback if no sessions appear. Safe Stop pauses future media actions; media-key actions are discrete and do not leave held key state behind.
