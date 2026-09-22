# Project review and validation — 2026-09-22

The review covered the WinUI shell, MIDI routing and discovery, output services, instrument providers, macro/game cancellation, local persistence, and build workflow. The main application remains an unpackaged .NET 10 / WinUI 3 x64 app.

## Findings addressed

| Area | Finding | Change |
| --- | --- | --- |
| Looper rendering | Large clones/overdub merges ran while holding the audio lock; native capture was copied in full | Publish immutable PCM, process outside the lock, read capture streams directly, reserve bounded capture buffers |
| Looper timing | Changed-tempo recordings used wall-clock sample lengths in the reference timeline | Resample captures into the common timeline, interpolate playback, fix step rounding, preserve manual BPM |
| Looper persistence | Concurrent saves overwrote shared PCM filenames; closing could save before analysis completed | Serialized generation saves with atomic manifest replacement; await finalization on shutdown; validate load sizes/paths |
| Audio lifecycle | Stale capture callbacks, inactive outputs, and default-device changes were not handled consistently | Detach/dispose capture safely, pause inactive looper output, refresh routes on endpoint changes |
| Custom | Three selectable banks did nothing | Persistent editor, chord validation, MIDI Learn, routing, and labels |
| Other banks | Per-app mixer, Presentation, Streaming, and vocal pitch buttons were incomplete | Implement audio-session selection/volume, presentation keys, configurable streaming hotkeys, semitone pitch buttons |
| Keyboard output | Keyboard-only INPUT unions had the wrong native size | Shared correctly sized Windows INPUT implementation, complete press/release batches, partial-send cleanup |
| Kaoss | Non-48 kHz files played at the wrong rate; only the left channel was imported; sample reads locked per frame | Mono/stereo downmix and 48 kHz resampling, interpolated reads, one render-buffer lock, stale-load protection |
| Macro/game safety | Safe Stop left macro tasks alive; Simon iterated a list cleared by Stop | Cancel and bound macro recording/playback; invalidate old Simon animation continuations |
| Diagnostics/shutdown | CSV writes flushed on every MIDI event; pending discovery could reopen ports after disposal | Bounded background logging; disposal guards after asynchronous port discovery; explicit window-close cleanup |
| Product surface | Selectable DJ/avatar banks advertised absent routing | Expose implemented integrations; document the remaining roadmap |

## Automated validation

Run `dotnet run --project Tests/SCSCompanion.RegressionTests.csproj -c Release`. The harness links the production services and uses synthetic PCM, fake shortcut output, and temporary session folders. It does not open a microphone or send real keyboard chords.

Regression coverage includes keyboard ABI sizing; shortcut parsing, persistence and repeated-note suppression; tempo-domain conversion; interpolation and loop wrapping; stereo output and paused transport; overdub/undo/redo; atomic session generations; finalization at shutdown; malformed/path-escaping session rejection; stereo 44.1 kHz import; macro cancellation; Simon cancellation; and mixer allocations.

The local synthetic benchmark rendered four 128-second tracks with half-speed FX at approximately **0.105 ms per 10 ms buffer**, with **zero steady-state allocated bytes across 2,000 buffers**. This measures DSP rendering cost, not physical microphone/output latency, and is not a before/after speedup claim. CI prints the timing without a brittle machine-speed assertion.

Release x64 build passes with zero warnings/errors. Regression checks run in Windows GitHub Actions. The app was launched and its main window and Custom settings editor inspected; invalid shortcut validation was exercised through the UI.

## Remaining limits

- Physical SCS.3d input/LED behavior and end-to-end audio latency need a connected-controller test; the local device was offline during the UI check.
- Tempo changes still alter pitch. Pitch-preserving time stretching, automatic latency calibration, and session import/export remain future work.
- Streaming hotkeys need assignment in the target application. Shortcut behavior ultimately depends on that application's bindings and focus.
- FLAC/OGG decoding depends on installed Windows Media Foundation codec support.
- The original monolithic page is retained to keep the review focused on working behavior; a full MVVM conversion is a separate refactor.
- Source changes do not replace the published 1.0.0 installer/portable download until a release is explicitly packaged and published.

The keyboard ABI fix follows Microsoft's [INPUT structure definition](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-input). Audio lifecycle handling was checked against [NAudio's WASAPI capture implementation](https://github.com/naudio/NAudio/blob/release/2.x/NAudio.Wasapi/WasapiCapture.cs).
