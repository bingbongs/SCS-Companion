using SCSCompanion.Models;

namespace SCSCompanion.Services;

public sealed class MouseTrackpadEngine : IDisposable
{
    private const string TrackpadSubmode = "Trackpad";
    private const string TrackballSubmode = "Trackball";
    private readonly MouseOutputService output = new();
    private readonly object stateLock = new();
    private readonly System.Threading.Timer driveTimer;
    private bool surfaceActive;
    private bool gainActive;
    private bool pitchActive;
    private DateTimeOffset surfaceStarted;
    private string activeSubmode = string.Empty;
    private int? surfacePosition;
    private int? previousSurfacePosition;
    private int? lastGainPosition;
    private int? lastPitchPosition;
    private double angularTravel;
    private double driveX;
    private double driveY;
    private double filteredTrackballDelta;
    private double sensitivity = 1.0;
    private readonly List<MacroAction> macro = [];
    private DateTimeOffset macroStarted;
    private DateTimeOffset b13Down;
    private CancellationTokenSource? playbackCancellation;
    public bool IsMacroRecording { get; private set; }
    public bool IsMacroPlaying { get; private set; }
    public bool HasMacro => macro.Count > 0;

    public MouseTrackpadEngine()
    {
        driveTimer = new System.Threading.Timer(OnDriveTick, null, TimeSpan.FromMilliseconds(16), TimeSpan.FromMilliseconds(16));
    }

    public event EventHandler<string>? ActionReported;

    public double Sensitivity
    {
        get
        {
            lock (stateLock)
            {
                return sensitivity;
            }
        }
        set
        {
            lock (stateLock)
            {
                sensitivity = Math.Clamp(value, 0.5, 2.0);
            }
        }
    }

    public static bool SupportsSubmode(string submode) =>
        string.Equals(submode, TrackpadSubmode, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(submode, TrackballSubmode, StringComparison.OrdinalIgnoreCase) || submode == "Presentation";

    public void Handle(MidiActivity activity, bool enabled, string submode)
    {
        if (!enabled || !SupportsSubmode(submode))
        {
            ResetTouches();
            return;
        }

        if (submode == "Presentation")
        {
            if (activity.Kind == "Note on")
            {
                ushort key = activity.Data1 switch { 0x6D => 0x74, 0x6E => 0x1B, 0x6F => 0x21, 0x70 => 0x22, 0x2C => 0x25, 0x2E => 0x27, 0x30 => 0x42, 0x32 => 0x57, _ => 0 };
                if (key != 0) Report(KeyboardOutput.SendChord(key), "Presentation shortcut");
            }
            return;
        }
        lock (stateLock)
        {
            activeSubmode = submode;
        }

        if (activity.Kind is "Note on" or "Note off")
        {
            var down = activity.Kind == "Note on";
            switch (activity.Data1)
            {
                case 0x62:
                    HandleSurfaceGate(down, activity.Timestamp);
                    return;
                case 0x07:
                    gainActive = down;
                    lastGainPosition = null;
                    return;
                case 0x03:
                    pitchActive = down;
                    lastPitchPosition = null;
                    return;
                case 0x2C:
                    Report(output.SetLeft(down), down ? "Left button down" : "Left button up");
                    Capture("left", down ? 1 : 0);
                    return;
                case 0x2E:
                    Report(output.SetRight(down), down ? "Right button down" : "Right button up");
                    Capture("right", down ? 1 : 0);
                    return;
                case 0x30:
                    HandleMacroButton(down, activity.Timestamp);
                    return;
                case 0x32:
                    Report(output.SetBack(down), down ? "Browser Back down" : "Browser Back up");
                    Capture("back", down ? 1 : 0);
                    return;
                case 0x6D when down:
                    if (IsMacroPlaying) StopMacro(); else if (HasMacro) _ = PlayMacroAsync();
                    return;
            }
        }

        if (activity.Kind != "Control")
        {
            return;
        }

        // C1 reports an absolute position around the ring on CC 0x62 and a
        // signed rotary delta around 64 on CC 0x63. It is not an X/Y pad.
        if (activity.Data1 == 0x62)
        {
            RouteSurfacePosition(activity.Data2);
            return;
        }

        if (activity.Data1 == 0x63 && surfaceActive &&
            string.Equals(submode, TrackballSubmode, StringComparison.OrdinalIgnoreCase))
        {
            RouteTrackballDelta(activity.Data2 - 64);
            return;
        }

        if (activity.Data1 == 0x07 && gainActive)
        {
            RouteScroll(activity.Data2, ref lastGainPosition, "GAIN");
            return;
        }

        if (activity.Data1 == 0x03 && pitchActive)
        {
            RouteScroll(activity.Data2, ref lastPitchPosition, "PITCH");
        }
    }

    public void ReleaseAll()
    {
        lock (stateLock)
        {
            StopMacro();
            IsMacroRecording = false;
            ResetTouches();
            output.ReleaseAll();
        }
    }

    private void HandleMacroButton(bool down, DateTimeOffset timestamp)
    {
        lock (stateLock)
        {
            if (down) { b13Down = timestamp; return; }
            if (timestamp - b13Down >= TimeSpan.FromMilliseconds(850))
            {
                StopMacro(); IsMacroRecording = false; macro.Clear(); ActionReported?.Invoke(this, "Macro erased"); return;
            }
            if (!IsMacroRecording)
            {
                StopMacro(); macro.Clear(); macroStarted = timestamp; IsMacroRecording = true; ActionReported?.Invoke(this, "Macro recording");
            }
            else
            {
                IsMacroRecording = false; ActionReported?.Invoke(this, $"Macro captured · {macro.Count} actions"); _ = PlayMacroAsync();
            }
        }

    }

    private void Capture(string kind, int a, int b = 0)
    {
        lock (stateLock)
        {
            if (!IsMacroRecording) return;
            var elapsed = DateTimeOffset.Now - macroStarted;
            if (elapsed > TimeSpan.FromMinutes(5) || macro.Count >= 20000)
            {
                IsMacroRecording = false;
                ActionReported?.Invoke(this, "Macro limit reached · recording stopped");
                return;
            }
            if (macro.Count > 0 && elapsed < macro[^1].At) elapsed = macro[^1].At;
            macro.Add(new MacroAction(elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed, kind, a, b));
        }
    }

    private async Task PlayMacroAsync()
    {
        if (macro.Count == 0 || IsMacroPlaying) return;
        playbackCancellation = new CancellationTokenSource(); var token = playbackCancellation.Token; IsMacroPlaying = true;
        MacroAction[] actions;
        lock (stateLock) actions = macro.ToArray();
        var owner = playbackCancellation;
        ActionReported?.Invoke(this, "Macro playing"); var previous = TimeSpan.Zero;
        try
        {
            foreach (var action in actions)
            {
                await Task.Delay(action.At - previous, token); previous = action.At;
                lock (stateLock)
                {
                    token.ThrowIfCancellationRequested();
                    if (action.Kind == "move") output.Move(action.A, action.B);
                    else if (action.Kind == "scroll") output.Scroll(action.A);
                    else if (action.Kind == "left") output.SetLeft(action.A != 0);
                    else if (action.Kind == "right") output.SetRight(action.A != 0);
                    else if (action.Kind == "back") output.SetBack(action.A != 0);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (stateLock)
            {
                if (ReferenceEquals(owner, playbackCancellation))
                {
                    output.ReleaseAll(); IsMacroPlaying = false;
                    playbackCancellation = null;
                    ActionReported?.Invoke(this, "Macro stopped");
                }
                owner?.Dispose();
            }
        }
    }

    private void StopMacro()
    {
        lock (stateLock)
        {
            playbackCancellation?.Cancel();
            playbackCancellation = null;
            IsMacroPlaying = false;
            output.ReleaseAll();
        }
    }

    public void Dispose()
    {
        ReleaseAll();
        driveTimer.Dispose();
    }

    private void HandleSurfaceGate(bool down, DateTimeOffset timestamp)
    {
        if (down)
        {
            lock (stateLock)
            {
                surfaceActive = true;
                surfaceStarted = timestamp;
                angularTravel = 0;
                surfacePosition = null;
                previousSurfacePosition = null;
                driveX = 0;
                driveY = 0;
                filteredTrackballDelta = 0;
            }
            return;
        }

        bool shouldClick;
        lock (stateLock)
        {
            shouldClick = surfaceActive && timestamp - surfaceStarted <= TimeSpan.FromMilliseconds(230) && angularTravel <= 4.5;
            surfaceActive = false;
            surfacePosition = null;
            previousSurfacePosition = null;
            driveX = 0;
            driveY = 0;
            filteredTrackballDelta = 0;
        }

        if (shouldClick)
        {
            Report(output.ClickLeft(), "Tap → left click");
            Capture("left", 1); Capture("left", 0);
        }
    }

    private void RouteSurfacePosition(int position)
    {
        lock (stateLock)
        {
            if (!surfaceActive)
            {
                return;
            }

            if (previousSurfacePosition is int previous)
            {
                var delta = Math.Abs(position - previous);
                angularTravel += Math.Min(delta, 128 - delta);
            }

            previousSurfacePosition = position;
            surfacePosition = position;
        }
    }

    private void RouteTrackballDelta(int delta)
    {
        if (Math.Abs(delta) <= 1)
        {
            return;
        }

        filteredTrackballDelta = (filteredTrackballDelta * 0.55) + (delta * 0.45);
        var moveX = (int)Math.Round(filteredTrackballDelta * 1.8 * Sensitivity);
        if (moveX != 0)
        {
            Report(output.Move(moveX, 0), $"Trackball {moveX:+0;-0;0}", reportSuccess: false);
            Capture("move", moveX, 0);
        }
    }

    private void RouteScroll(int position, ref int? previousPosition, string source)
    {
        if (previousPosition is int previous)
        {
            var delta = position - previous;
            if (Math.Abs(delta) <= 32 && delta != 0)
            {
                Report(output.Scroll(delta * 18), $"{source} scroll {(delta > 0 ? "up" : "down")}");
                Capture("scroll", delta * 18);
            }
        }

        previousPosition = position;
    }

    private void OnDriveTick(object? state)
    {
        int moveX;
        int moveY;
        lock (stateLock)
        {
            if (!surfaceActive || surfacePosition is not int position ||
                !string.Equals(activeSubmode, TrackpadSubmode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Position 0 is treated as the top of the ring and values progress
            // clockwise. Smooth toward that direction so changing sectors does
            // not produce a sharp cursor corner.
            var angle = (position / 128d * Math.Tau) - (Math.PI / 2d);
            var targetX = Math.Cos(angle);
            var targetY = Math.Sin(angle);
            driveX += (targetX - driveX) * 0.28;
            driveY += (targetY - driveY) * 0.28;

            var heldMilliseconds = (DateTimeOffset.Now - surfaceStarted).TotalMilliseconds;
            var speed = (2.5 + Math.Min(heldMilliseconds / 700d, 1d) * 6.5) * sensitivity;
            moveX = (int)Math.Round(driveX * speed);
            moveY = (int)Math.Round(driveY * speed);
        }

        lock (stateLock)
        {
            if (!surfaceActive) return;
            _ = output.Move(moveX, moveY);
            Capture("move", moveX, moveY);
        }
    }

    private void ResetTouches()
    {
        lock (stateLock)
        {
            surfaceActive = false;
            gainActive = false;
            pitchActive = false;
            activeSubmode = string.Empty;
            surfacePosition = null;
            previousSurfacePosition = null;
            lastGainPosition = null;
            lastPitchPosition = null;
            angularTravel = 0;
            driveX = 0;
            driveY = 0;
            filteredTrackballDelta = 0;
        }
    }

    private void Report(bool success, string action, bool reportSuccess = true)
    {
        if (!success || reportSuccess)
        {
            ActionReported?.Invoke(this, success ? action : $"Blocked · {action}");
        }
    }

    private sealed record MacroAction(TimeSpan At, string Kind, int A, int B);
}
