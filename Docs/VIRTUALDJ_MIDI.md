# VirtualDJ virtual-MIDI profile

The `FX` → `DJ` → `VirtualDJ` bank uses SCS Companion's isolated channel-16 DJ MIDI messages. It provides a compact deck-utility layer while the physical SCS.3d remains exclusively owned by the companion.

## Automatic installation

Open the companion's settings cog and select **Install profile** under **VirtualDJ Setup**. The button detects VirtualDJ in `%ProgramFiles%\VirtualDJ` or `%ProgramFiles(x86)%\VirtualDJ`, then installs both files required by VirtualDJ:

- the device definition in `Documents\VirtualDJ\Devices`;
- the action mapper in `Documents\VirtualDJ\Mappers`.

The button can repair or update either copy. Restart VirtualDJ afterward, open **Settings → Controllers**, choose **SCS Companion DJ Utilities**, and ensure the mapping is not set to **Ignore**.

## Manual fallback

If automatic copying fails, copy [the bundled definition](../Profiles/VirtualDJ/Devices/SCS%20Companion%20DJ%20Utilities.xml) to `Documents\VirtualDJ\Devices` and [the bundled mapper](../Profiles/VirtualDJ/Mappers/SCS%20Companion%20DJ%20Utilities.xml) to `Documents\VirtualDJ\Mappers`. Create either folder if it does not exist, then restart VirtualDJ.

## Deck utility layout

| SCS.3d control | VirtualDJ action |
| --- | --- |
| PLAY | Play/Pause |
| CUE | Cue/Stop |
| SYNC | Sync |
| TAP | Beat tap |
| B11 | Key Lock / master tempo |
| B12 | Quantize all |
| B13 | Slip mode |
| B14 | Censor/dump while held |
| GAIN strip | Deck gain |
| PITCH strip | Pitch/tempo |
| Center strip | Deck volume |
| Ring clockwise | Beatjump forward one beat |
| Ring counterclockwise | Beatjump backward one beat |

The current development definition identifies the existing `From SCS.3 DaRouter` compatibility endpoint. DaRouter does not need to be running. The release definition will target the app-owned `SCS Companion MIDI` endpoint when the production standalone Windows MIDI transport replaces that compatibility port.

VirtualDJ may require a controller-capable license for uninterrupted external-controller use. Its Home configuration can limit controller operation to a short evaluation period.
