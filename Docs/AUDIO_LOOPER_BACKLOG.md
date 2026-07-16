# Audio looper backlog

Audio Looper is planned as a repeated-press submode of the physical `LOOP` button, alongside the existing productivity and macro profiles.

## Intended first version

- Capture the current Windows default microphone through WASAPI shared mode.
- Keep the loop local and in memory with an explicit maximum duration.
- Provide Record, Play/Stop, Overdub, Undo last layer, Clear, and input-level feedback.
- Follow Windows default-device changes automatically, with a manual device override in settings.
- Disable monitoring by default to prevent speaker-to-microphone feedback; make monitoring and its output device explicit.
- Release and stop audio cleanly on mode changes, disconnect, suspend, safe stop, and app exit.

## Candidate SCS.3d layout

| Control | Initial action |
| --- | --- |
| PLAY | Play / stop loop |
| CUE | Start / stop recording |
| SYNC | Toggle overdub |
| TAP | Tap tempo / quantized-length option |
| B11 | Undo last layer |
| B12 | Redo or duplicate layer |
| B13 | Clear, with hold confirmation |
| B14 | Monitoring toggle |
| GAIN | Input gain |
| PITCH | Loop output level |
| Main surface | Loop position or layer mix |

Implementation follows the core input, output-safety, and LED state engines so recording state can be represented reliably on both the app and hardware.
