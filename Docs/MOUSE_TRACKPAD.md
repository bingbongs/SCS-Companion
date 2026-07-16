# Mouse control prototype

The active mouse output slice is `VINYL` → `Mouse`. DaRouter is not required.

The live capture clarified the circle's protocol: CC `0x62` is the absolute position around the C1 ring, while CC `0x63` is a signed rotary delta centered on 64. These are not independent X/Y coordinates, so the circle cannot report a laptop touchpad finger position directly.

## Submodes

Repeated presses of VINYL cycle these profiles:

1. **Trackpad** — holding a point on the ring continuously pushes the cursor in that direction. Top, bottom, left, right, and diagonals provide full two-dimensional movement. Motion starts precise and accelerates over 700 ms for longer screen travel. Moving around the ring smoothly steers the cursor.
2. **Trackball** — clockwise/counterclockwise ring motion becomes smooth horizontal pointer travel. This preserves the useful rotary behavior as a secondary profile and uses the controller's native relative-delta stream.
3. **Presentation** — reserved for a later presentation-control mapping; pointer output is currently inactive.

The settings cog opens a compact flyout beside the device. Pointer sensitivity ranges from 50–200%, affects both Trackpad and Trackball travel immediately, and is restored at the next launch.

## Shared mapping

| Hardware input | Windows action |
| --- | --- |
| GAIN strip | Vertical wheel scroll |
| PITCH strip | Vertical wheel scroll |
| Quick tap on the circular surface | Left click |
| Upper-left round button (B11) | Left mouse button |
| Upper-right round button (B12) | Right mouse button |
| Lower-left round button (B13) | Middle mouse button |
| Lower-right round button (B14) | Browser Back button |

The center region is intentionally no longer assigned to scrolling. The round mouse buttons preserve press and release, so B11 supports dragging.

## Safety and Windows limitation

The footer routing control must read `MOUSE LIVE` for actions to leave the app. `PAUSED` still permits device observation without moving or clicking the pointer. Safe Stop, mode changes, pause, disconnect, and exit stop continuous movement and release held mouse buttons.

Windows blocks `SendInput` from controlling applications running at a higher integrity level. SCS Companion intentionally runs without administrator privileges, so it will not control an elevated application unless both processes run at the same integrity level.
