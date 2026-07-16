# Four-track audio looper

The default `LOOP` hardware slot loads the standalone `Looper` module. It opens the current Windows default recording endpoint in shared WASAPI mode and captures in that endpoint's native format. This supports Windows defaults routed through VoiceMeeter without forcing an incompatible sample rate or buffer format. Only the first two input channels are used; they are downmixed once and resampled to the looper's 48 kHz stereo engine. Loop audio remains in memory and plays through the default output device. DaRouter is not involved.

## Track and action layout

| SCS.3d control | Looper action |
| --- | --- |
| PLAY / T1 | Track 1: record when empty; short press after capture toggles stop/play; hold to erase |
| CUE / T2 | Track 2: record when empty; short press after capture toggles stop/play; hold to erase |
| SYNC / T3 | Track 3: record when empty; short press after capture toggles stop/play; hold to erase |
| TAP / T4 | Track 4: record when empty; short press after capture toggles stop/play; hold to erase |
| B11 | Start/end a dub layer; while a punch effect is active, latch/release that effect |
| B12 | Play all completed tracks |
| B13 | Tap tempo normally; Undo after DUB exposes an available undo; hold as SHIFT |
| B14 | Stop all tracks; SHIFT + B14 cycles Reverb, Dub Echo, Robot, and Off |
| GAIN | Master looper output level |
| PITCH | Manual tempo, 60–180 BPM |
| Center strip | Punch-effect wet depth; SHIFT changes live-input FX depth |
| Left strip, upper/lower | Toggle punch FX routing for tracks 1/2 |
| Right strip, upper/lower | Toggle punch FX routing for tracks 3/4 |

An empty track begins recording on the button-down event, so no initial audio is lost. Pressing it again finishes the capture. Once audio exists, a short press toggles that track between playing and stopped. Holding for 550 ms arms erase and rapidly flashes the track; continuing to hold for another 450 ms permanently clears it. Releasing during the warning flash cancels the erase.

The transport row uses its actual two-color hardware protocol: blue means empty/idle, flashing red/blue means recording, and steady red means that track contains a completed loop. Playing versus stopped is written directly on the application's transport label because the hardware transport lamps only expose red and blue.

## Momentary punch effects

Touch and hold a quadrant of the circular surface to affect every track whose FX route is enabled. All four routes start enabled. Releasing the surface immediately clears the momentary selection and its ring LED. Press B11 while touching an effect to latch it; press B11 again to release the latch.

| Ring direction | Effect |
| --- | --- |
| Top | 1/8-second stutter |
| Right | Reverse |
| Bottom | Half-speed |
| Left | Rhythmic gate |

The first recording is analyzed using onset peaks plus an autocorrelation fallback to estimate BPM. Its ending is quantized to the nearest sixteenth-note step, up to 512 steps. B13 flashes on the beat and accepts manual tempo taps; PITCH remains a 60–180 BPM override.

Tap tempo now aligns both rate and phase. Each tap re-anchors the master beat and automatically advances it by the looper's 60 ms shared-output buffer, allowing the looper to follow external playback such as Spotify instead of merely matching its BPM. Settings → General provides an additional persistent -250 to +250 ms correction; positive values advance a looper that is still heard late on a particular audio stack.

Each track has its own quantized length and phase anchor on the shared transport. A 16-step track can therefore play alongside 32-, 64-, or longer step tracks without being padded or truncated to the first recording. Overdubs wrap inside only their selected track. Moving away from the Looper module or using Safe Stop finalizes an active recording, releases punch effects, and closes live microphone effects.

Leaving Looper mode silences its mixer and freezes the musical transport. Returning to Looper resumes the saved tracks from that position. Track PCM, step lengths, phase anchors, mute and FX-routing states, selected track, BPM, transport position, output level, and effect depths persist under the current user's local application data. Audio input/output selections and sync correction persist separately and default to following Windows.

Tempo adjustment currently behaves like tape-speed control, so changing BPM also changes pitch. Independent time-stretching is a later audio-engine improvement.

SHIFT + B14 enables wet-only live microphone effects even when no loop is playing or recording. Dry microphone monitoring remains disabled, so only the selected Reverb, tempo-linked Dub Echo, or Robot effect reaches the output. Headphones are strongly recommended because wet output can still be recaptured by speakers.

## Module slots

Settings now has a `Modules` tab. Each printed hardware bank can host any available module. Selecting a module already assigned elsewhere swaps the two banks; selecting the currently inactive module replaces that bank's previous module. Assignments persist under `%LOCALAPPDATA%\SCSCompanion\module-assignments.json`.

Settings is organized into `General`, `Modules`, and `DJ`. General begins with Windows-default-or-specific output and microphone selection plus external sync correction. DJ contains virtual MIDI status and the Mixxx and VirtualDJ profile installers.
