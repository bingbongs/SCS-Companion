# Standalone DJ MIDI transport

SCS Companion's release requirement is one application and one controller. DaRouter is not a runtime component, will not be bundled under another name, and is not part of the intended friend-to-friend installation flow.

## Transport strategy

1. Prefer an app-owned endpoint named `SCS Companion MIDI` using the production Windows MIDI Services Virtual Device transport.
2. Keep legacy DaRouter, Bome, or loopMIDI endpoints detectable only as development/compatibility paths for existing machines.
3. Never silently download or install an unsigned preview MIDI driver.
4. Surface transport readiness in Settings and keep DJ banks in `OBSERVE` when no safe endpoint exists.

Microsoft's current Windows MIDI Services SDK exposes app-to-app virtual devices and temporary loopback endpoints. The loopback transport must be installed and enabled, and the publicly documented SDK/runtime packages remain release-candidate or preview dependencies as of this development checkpoint. This PC has Windows build 26200 but does not currently have the SDK runtime/tools installed. The companion should integrate the production runtime only when it is safe to redistribute and test across supported Windows releases.

The Mixxx profile installer is independent of that transport work and is already built into Settings. Its mapping and manual recovery instructions ship with the application.
