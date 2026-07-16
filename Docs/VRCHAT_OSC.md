# VRChat native OSC profile

The first universal VRChat slice uses only the native `/input/*` OSC API. It does not require avatar parameters, a prefab, or any avatar modification.

Enable OSC inside VRChat's Action Menu, then select `TRIG` → `VRChat`. The footer reads `VR OSC` in Auto, Desktop, and PC VR submodes. Messages are sent to VRChat's default receiver at `127.0.0.1:9000`.

## Shared controls

| SCS.3d control | VRChat input |
| --- | --- |
| Circular surface | Two-dimensional movement (`Horizontal` + `Vertical`) |
| Center strip | Precision, 5–100%, shared by walking and turning |
| PITCH strip | Smooth horizontal look/turn while touched, scaled by precision |
| PLAY | Jump |
| CUE | Run |
| SYNC | Voice |
| TAP | Right Quick Menu toggle |

The ring uses its absolute sector as a movement direction: top is forward, bottom backward, and the sides strafe. Releasing the ring immediately sends both movement axes back to zero.

## Soft buttons

| Button | Input |
| --- | --- |
| B11 / B12 | Variable-speed turn left / right, scaled by the center strip |
| B13 | Open/close the left radial menu (`QuickMenuToggleLeft`) |
| B14 | Open/close the right Quick Menu and toggle its temporary full-direction pointer layer |

While B14's pointer layer is active, the circular surface controls the Windows pointer using the directional Trackpad engine instead of sending movement to VRChat. Press B14 again to close the menu and release the pointer layer. The footer reads `VR MENU MOUSE` while it is active.

Auto currently uses the desktop-safe layout. Automatic Desktop-versus-PC-VR detection remains a later refinement; the manual submode always takes priority. The shared soft-button layout is deliberately consistent across Auto, Desktop, and PC VR.

Avatar Actions intentionally remains inactive until the companion reads the current avatar's generated OSC configuration and can distinguish supported parameters from unavailable ones.

Every axis is reset to `0.0` and every button to `0` on contact release, mode/submode change, routing pause, Safe Stop, controller disconnect, and application exit.
