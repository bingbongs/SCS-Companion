# SCS.3d protocol notebook

This file records working observations from the installed DaRouter 1.2.43 presets, bundled documentation, the existing Mixxx SCS.3d mapping, and live Windows MIDI enumeration. Values remain provisional until verified against physical input in SCS Companion.

## Confirmed environment

- USB identity: `VID_0D60&PID_7901`
- Windows MIDI endpoint name: `SCS.3d`
- Installed legacy application: `C:\Program Files (x86)\Stanton SCS.3 DaRouter\SCS3_DaRouter.exe`
- Legacy presets: `C:\ProgramData\Stanton SCS.3 DaRouter`
- Manufacturer SysEx prefix seen in presets: `F0 00 01 60`

## Physical mode input notes

These Note On values are consistent across the inspected DaRouter presets and the current app mapping.

| Physical label | Note (hex) | Companion mode |
| --- | ---: | --- |
| FX | `20` | DJ |
| LOOP | `22` | Productivity |
| VINYL | `24` | Mouse |
| EQ | `26` | Media |
| TRIG | `28` | VRChat |
| DECK | `2A` | Custom |

Known soft/transport notes currently visualized:

- B11–B14: `2C`, `2E`, `30`, `32`
- PLAY, CUE, SYNC, TAP: `6D`, `6E`, `6F`, `70`

## Surface configuration leads

### Live capture: 2026-07-13

A physical surface sweep produced paired channel-1 controls `CC 0x62` and `CC 0x63`, followed by `Note Off 0x62` when contact ended. Subsequent mouse testing and the established Mixxx mapping clarified their roles: `0x62` is absolute position around C1, while `0x63` is a signed rotary delta centered on 64. They are not an X/Y pair.

```text
B0 62 32
B0 63 43
...
80 62 00
```

The continuous surface stream displaced earlier button events from the in-memory monitor. Builds after this capture persist the complete session as CSV under `%LOCALAPPDATA%\SCSCompanion\Captures`.

The following full 2,019-event capture verified the major input families:

| Physical control | Touch gate | Coordinate/value controls |
| --- | ---: | --- |
| GAIN strip | Note `07` | CC `07` + `08` |
| PITCH strip | Note `03` | CC `03` + `04` |
| Main circular region | Note `62` | CC `62` + `63` |
| Center strip / second region | Note `01` | CC `01` + `02` |

The side-strip primary axes reached the full 0–127 range while their secondary axes stayed near the center, confirming position pairs rather than ordinary one-dimensional faders. Notes `01` and `62` were observed concurrently during the two-finger test. This verifies simultaneous reporting from two touch regions; it does not yet prove that two arbitrary contacts on the same region are independently tracked.

The same capture independently verified mode notes `20`, `22`, `24`, `26`, `28`, `2A`; soft-key notes `2C`, `2E`, `30`, `32`; and transport notes `6D`–`70`, all with velocity `01` on press and Note Off on release.

The existing Mixxx mapping documents a command shaped like:

```text
F0 00 01 60 01 <mode> F7
```

Observed mode byte meanings:

| Byte | Surface interpretation |
| ---: | --- |
| `00` | C1 circular control |
| `01` | S5 slider |
| `02` | S3 slider |
| `03` | combined S3 + S5 |
| `04` | button regions |

DaRouter presets also contain jitter-control messages shaped like `F0 00 01 60 02 <control> <jitter> <jitter> F7`. Exact control identifiers and useful thresholds must be established with live tests before the companion sends them.

## LED output

The maintained Mixxx mapping and DaRouter material agree on the mode and soft-button color values: Note On velocity `01` is red, `02` is blue, `03` is purple, and `00` is black/off. The transport family is different: PLAY/CUE/SYNC/TAP use velocity `00` for blue and `01` for red; values `02` and `03` must not be used as transport colors because the unit displays them as red. The companion therefore cycles three-color feedback only on capable buttons, while the transport row uses blue idle/red active in every module.

The maintained mapping also confirms the segmented feedback ranges now mirrored by the companion: GAIN uses `CC 07` values `28`–`31` hex, PITCH uses `CC 03` values `14`–`1D` hex, the center S4 strip accepts a level on `CC 01`, and the circular C1 position LED accepts `01`–`10` hex on `CC 62`.

`CC 7B 00` is used as the all-notes/all-LED clear operation during output teardown.

## Design constraints discovered

- The surface is multi-touch-capable, but legacy material demonstrates simultaneous regions/gestures rather than Windows Precision Touchpad packets. Laptop-like behavior should therefore be synthesized from MIDI coordinates and contact state.
- Serato's user MIDI mapping can cover many secondary controls, but user-mapped platter behavior is restricted. The Serato profile should prioritize missing utility functions such as Key Lock and avoid promising native platter support without an official hardware integration.
- VRChat's universal OSC input namespace can support native movement/menu-style actions. Avatar parameter actions are conditional on the loaded avatar and must be clearly marked as such in the UI.

## Next capture session

Capture each control independently and record press, release, hold, single-touch movement, two-touch behavior, and LED response. The observation build already shows raw MIDI bytes in its activity monitor; output injection remains disabled during this step.
