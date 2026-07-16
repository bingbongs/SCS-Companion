# Serato virtual-MIDI profile

The first DJ output slice is active only in `FX` → `DJ` → `Serato`. Repeatedly press FX until the footer reads `DJ MIDI`. All other modes and DJ targets leave this virtual output silent.

SCS Companion automatically opens `To SCS.3 DaRouter`, which feeds the existing `2 - From SCS.3 DaRouter` MIDI input visible to Serato. DaRouter itself does not need to be running. If that endpoint is unavailable, the service can fall back to an installed Bome or loopMIDI output.

## Serato MIDI Learn assignments

In Serato's MIDI mapping panel, select or enable `2 - From SCS.3 DaRouter`, choose each Serato control, then press the corresponding SCS.3d control.

| SCS.3d control | Suggested Serato assignment | Emitted message |
| --- | --- | --- |
| PLAY | Play/Pause | Ch 16 Note 16 |
| CUE | Cue | Ch 16 Note 17 |
| SYNC | Sync | Ch 16 Note 18 |
| TAP | Tap tempo | Ch 16 Note 19 |
| B11 | Key Lock / Master Tempo | Ch 16 Note 32 |
| B12 | Quantize | Ch 16 Note 33 |
| B13 | Slip | Ch 16 Note 34 |
| B14 | Censor / Reverse | Ch 16 Note 35 |
| GAIN strip | User choice: Key Shift or FX depth | Ch 16 CC 40 |
| PITCH strip | User choice: tempo or secondary utility | Ch 16 CC 41 |
| Center strip | User choice: loop size or library position | Ch 16 CC 42 |
| Ring clockwise | Beat Jump forward | Ch 16 Note 48 pulses |
| Ring counterclockwise | Beat Jump backward | Ch 16 Note 49 pulses |

Channel 16 keeps the companion's translated messages separate from common channel-1 controller mappings. Mode changes, routing pause, Safe Stop, disconnect, and exit release every held translated note.

Serato does not permit ordinary user MIDI mapping to its virtual platters, so the ring intentionally defaults to Beat Jump rather than promising scratch control.
