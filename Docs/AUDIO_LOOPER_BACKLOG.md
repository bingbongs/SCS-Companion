# Audio looper status

The four-track looper is implemented. See [the current control guide](FOUR_TRACK_LOOPER.md) and [review results](PROJECT_REVIEW.md). This file previously described an obsolete single-track plan.

Completed: four independent loop lengths, overdub/undo, bounded native-format capture, manual/estimated tempo, output/input selection, live effects, input metering, persistent sessions, safe stop, and device-change refresh.

Remaining audio roadmap:

- Pitch-preserving time stretching (tempo still changes pitch).
- Measured round-trip latency calibration; current compensation is a configurable estimate.
- Loop import/export and multi-session management.
- Hardware soak tests across VoiceMeeter, USB microphones, Bluetooth output, and hot-unplug scenarios.
