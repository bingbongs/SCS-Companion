# Mixxx 2.5 virtual-MIDI profile

The `FX` → `DJ` → `Mixxx` bank uses SCS Companion's DJ MIDI transport. The development machine currently exposes a legacy compatibility endpoint, but the release architecture does not require DaRouter to run and will prefer the app-owned `SCS Companion MIDI` endpoint as soon as the production Windows MIDI Services transport is bundled.

## Automatic installation

Open the companion's settings cog and select **Install profile** under **Mixxx Setup**. The button detects Mixxx in `%ProgramFiles%\Mixxx` or `%ProgramFiles(x86)%\Mixxx`, then copies the bundled mapping into `%LOCALAPPDATA%\Mixxx\controllers`. It can also repair or update an existing copy. Reopen **Preferences → Controllers**, select the SCS Companion/compatibility MIDI input, load **SCS Companion DJ Utilities**, enable it, and click **Apply**.

## Manual fallback

If detection or copying fails, copy [SCS Companion DJ Utilities.midi.xml](../Profiles/Mixxx/SCS%20Companion%20DJ%20Utilities.midi.xml) to `%LOCALAPPDATA%\Mixxx\controllers` yourself. Create the `controllers` folder if it does not exist. Close and reopen Mixxx Preferences afterward so the mapping list refreshes. Do not enable the physical `SCS.3d` entry at the same time; the companion owns that endpoint and forwards its translated bank.

## Deck 1 utility layout

| SCS.3d control | Mixxx 2.5 control |
| --- | --- |
| PLAY | Play/Pause |
| CUE | Default cue |
| SYNC | Sync enabled |
| TAP | BPM tap |
| B11 | Keylock / master tempo |
| B12 | Quantize |
| B13 | Slip |
| B14 | Reverse roll |
| GAIN strip | Pregain |
| PITCH strip | Tempo rate |
| Center strip | Channel volume |
| Ring clockwise | Beatjump forward one beat |
| Ring counterclockwise | Beatjump backward one beat |

The mapping targets the locally installed Mixxx 2.5.3 and declares compatibility with Mixxx 2.5 or newer. The companion sends translated events on MIDI channel 16 so they remain isolated from common channel-1 mappings.
