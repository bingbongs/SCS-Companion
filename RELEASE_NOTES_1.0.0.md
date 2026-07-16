# SCS Companion 1.0.0

The first public release turns the Stanton SCS.3d / DaScratch into a standalone Windows control surface without requiring DaRouter.

## Highlights

- Direct SCS.3d MIDI input, LED output, reconnect handling, diagnostics, and safe stop.
- Six persistent, reassignable physical module slots.
- Mouse, VRChat OSC, DJ, Media, Productivity, Looper, Kaoss, Audacity, Discord, Simon, Pink Trombone, and Custom modules.
- Four-track synchronized microphone looper with persistent sessions and performance effects.
- Mixxx and VirtualDJ profile installation/repair from the settings flyout.
- Self-contained x64 installer and portable package.
- Compact device-shaped WinUI interface with five settings pages and multiple themes.

## Downloads

- **Setup:** `SCSCompanion-Setup-1.0.0-win-x64.exe`
- **Portable:** `SCSCompanion-Portable-1.0.0-win-x64.zip`

The binaries are not code-signed, so Windows SmartScreen may ask for confirmation. The installer is per-user and does not require administrator privileges.

## Known limitations

- This release targets Windows x64.
- One physical SCS.3d is supported at a time.
- Some DJ profiles still rely on a compatible virtual MIDI endpoint supplied by the user's Windows audio/MIDI setup.
- Application shortcut modules depend on the target application's current shortcut behavior.
- The Pink Trombone module is a native formant/vocal-tract instrument inspired by the original concept, not an embedded copy of the web application.

See the README and bundled documentation for module controls, setup, attribution, and the roadmap.
