using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SCSCompanion.Models;

namespace SCSCompanion.Services;

public sealed class AudioLooperService : IDisposable
{
    private const int SampleRate = 48000;
    private const int MaximumSteps = 512;
    private const int OutputBufferMilliseconds = 60;
    private readonly object syncRoot = new();
    private readonly LoopTrack[] tracks = [new(), new(), new(), new()];
    private readonly LoopMixProvider mixProvider;
    private WasapiCapture? capture;
    private BufferedWaveProvider? liveInputBuffer;
    private WaveFormat? captureFormat;
    private string captureDeviceName = "Default microphone";
    private IWavePlayer? output;
    private MemoryStream? recordingBytes;
    private WaveFormat? recordingFormat;
    private long recordingStartPosition;
    private int selectedTrack;
    private int recordingTrack;
    private volatile bool recording;
    private volatile bool finalizing;
    private Task finalizationTask = Task.CompletedTask;
    private readonly object saveLock = new();
    private volatile bool disposed;
    private readonly object deviceLock = new();
    private bool manualTempo;
    private double recordingSpeed = 1;
    private long maximumRecordingBytes;
    public float InputPeak => mixProvider.InputPeak;
    private bool muteRecordingOnFinalize;
    private bool stopAllRequested;
    private bool automaticStopQueued;
    private bool surfaceActive;
    private bool stripActive;
    private int stripX = 64;
    private int stripY = 64;
    private PunchEffect punchEffect;
    private PunchEffect heldPunchEffect;
    private bool dubMode;
    private bool shiftHeld;
    private bool shiftChordUsed;
    private readonly Queue<DateTime> tempoTaps = new();
    private LiveInputEffect liveInputEffect;
    private string? configuredInputDeviceId;
    private string? configuredOutputDeviceId;
    private int syncCompensationMilliseconds;
    private readonly string sessionDirectory;
    private readonly string sessionStatePath;
    private CancellationTokenSource? sessionSaveCancellation;
    private int audioRevision;
    private int savedAudioRevision = -1;
    private CancellationTokenSource? transportHoldCancellation;
    private int heldTrack = -1;
    private bool ignoreTransportRelease;
    private int eraseArmedTrack = -1;

    public AudioLooperService(string? inputDeviceId = null, string? outputDeviceId = null, int syncCompensationMs = 0, string? storageDirectory = null)
    {
        configuredInputDeviceId = inputDeviceId;
        configuredOutputDeviceId = outputDeviceId;
        syncCompensationMilliseconds = Math.Clamp(syncCompensationMs, -250, 250);
        mixProvider = new LoopMixProvider(syncRoot, tracks, () => punchEffect);
        sessionDirectory = storageDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCSCompanion", "Looper");
        sessionStatePath = Path.Combine(sessionDirectory, "session.json");
        Directory.CreateDirectory(sessionDirectory);
        LoadSession();
        StateChanged += (_, _) => ScheduleSessionSave();
    }

    public event EventHandler<string>? ActionReported;
    public event EventHandler? StateChanged;

    public int SelectedTrack => selectedTrack;
    public int RecordingTrack => recordingTrack;
    public bool IsRecording => recording;
    public bool IsFinalizing => finalizing;
    public double Bpm => mixProvider.TempoBpm;
    public double BeatPhase => output is null
        ? DateTime.UtcNow.TimeOfDay.TotalSeconds * mixProvider.TempoBpm / 60d % 1d
        : mixProvider.BeatPhase;
    public bool BpmWasDetected { get; private set; }
    public int EraseArmedTrack => eraseArmedTrack;
    public bool IsDubMode => dubMode;
    public bool IsShiftHeld => shiftHeld;
    public bool IsPunchHeld => heldPunchEffect != PunchEffect.None;
    public bool IsPunchActive => punchEffect != PunchEffect.None;
    public bool CanUndo
    {
        get { lock (syncRoot) return tracks[selectedTrack].UndoSamples.Length > 0; }
    }
    public string LiveEffectName => liveInputEffect switch
    {
        LiveInputEffect.Reverb => "REVERB",
        LiveInputEffect.DubEcho => "DUB ECHO",
        LiveInputEffect.Robot => "ROBOT",
        _ => "OFF",
    };
    public int SyncCompensationMilliseconds => syncCompensationMilliseconds;
    public bool IsSelectedTrackMuted
    {
        get
        {
            lock (syncRoot) return tracks[selectedTrack].Muted;
        }
    }
    public byte SelectedTrackNote => (byte)(0x6D + selectedTrack);
    public bool HasTrackAudio(int index)
    {
        lock (syncRoot) return tracks[index].Samples.Length > 0;
    }

    public int GetTrackSteps(int index)
    {
        lock (syncRoot) return tracks[index].Steps;
    }

    public bool IsTrackFxEnabled(int index)
    {
        lock (syncRoot) return tracks[index].FxEnabled;
    }

    public void SetActive(bool active)
    {
        if (disposed) return;
        if (!active)
        {
            mixProvider.SetActive(false);
            ReleaseAll();
            output?.Pause();
        }
        else
        {
            try
            {
                EnsureOutput();
                if (liveInputEffect != LiveInputEffect.None) EnsureCapture();
            }
            catch (Exception exception) { Report($"Looper output unavailable · {exception.Message}"); }
        }
        mixProvider.SetActive(active);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ConfigureAudioDevices(string? inputDeviceId, string? outputDeviceId, int compensationMilliseconds)
    {
        configuredInputDeviceId = string.IsNullOrWhiteSpace(inputDeviceId) ? null : inputDeviceId;
        configuredOutputDeviceId = string.IsNullOrWhiteSpace(outputDeviceId) ? null : outputDeviceId;
        syncCompensationMilliseconds = Math.Clamp(compensationMilliseconds, -250, 250);
        if (recording) StopRecording();
        StopCapture();
        output?.Stop();
        output?.Dispose();
        output = null;
        Report($"Audio routing updated · sync {syncCompensationMilliseconds:+0;-0;0} ms");
    }

    public void Handle(MidiActivity activity, bool enabled)
    {
        if (!enabled || disposed) return;

        if (activity.Kind is "Note on" or "Note off")
        {
            var down = activity.Kind == "Note on";
            if (activity.Data1 == 0x62)
            {
                surfaceActive = down;
                if (!down)
                {
                    SetPunchEffect(heldPunchEffect);
                }
                return;
            }

            if (activity.Data1 == 0x01)
            {
                if (!down && stripActive) ToggleStripFxRoute();
                if (down) { stripX = 64; stripY = 64; }
                stripActive = down;
                return;
            }

            if (activity.Data1 is >= 0x6D and <= 0x70)
            {
                HandleTrackButton(activity.Data1 - 0x6D, down);
                return;
            }

            switch (activity.Data1)
            {
                case 0x2C:
                    if (down) HandleDubOrHold();
                    break;
                case 0x2E:
                    if (down) PlayAll();
                    break;
                case 0x30:
                    HandleShiftTapUndo(down);
                    break;
                case 0x32:
                    if (down)
                    {
                        if (shiftHeld)
                        {
                            shiftChordUsed = true;
                            CycleLiveInputEffect();
                        }
                        else StopAll();
                    }
                    break;
            }
            return;
        }

        if (activity.Kind != "Control")
        {
            return;
        }

        if (activity.Data1 == 0x07)
        {
            mixProvider.MasterVolume = activity.Data2 / 127f * 1.2f;
            Report($"Looper output {mixProvider.MasterVolume * 100:0}%");
            ScheduleSessionSave();
        }
        else if (activity.Data1 == 0x03)
        {
            var tempo = 60d + (activity.Data2 / 127d * 120d);
            if (recording || finalizing) { Report("Finish recording before changing tempo"); return; }
            manualTempo = true;
            BpmWasDetected = false;
            mixProvider.SetTempo(tempo);
            Report($"Tempo {mixProvider.TempoBpm:0.0} BPM");
            ScheduleSessionSave();
        }
        else if (activity.Data1 == 0x02 && stripActive)
        {
            stripX = activity.Data2;
        }
        else if (activity.Data1 == 0x01 && stripActive)
        {
            stripY = activity.Data2;
            if (stripX is >= 42 and <= 86)
            {
                var depth = activity.Data2 / 127f;
                if (shiftHeld) { shiftChordUsed = true; mixProvider.LiveEffectDepth = depth; }
                else mixProvider.EffectDepth = depth;
                Report(shiftHeld
                    ? $"Live FX depth {mixProvider.LiveEffectDepth * 100:0}%"
                    : $"Punch depth {mixProvider.EffectDepth * 100:0}%");
                ScheduleSessionSave();
            }
        }
        else if (activity.Data1 == 0x62 && surfaceActive)
        {
            var sector = ((activity.Data2 + 16) / 32) % 4;
            SetPunchEffect((PunchEffect)(sector + 1));
        }
    }

    public string GetTrackState(int index)
    {
        lock (syncRoot)
        {
            if (finalizing && recordingTrack == index) return "FINISHING";
            if (recording && recordingTrack == index) return tracks[index].Samples.Length == 0 ? "RECORDING" : "OVERDUBBING";
            if (tracks[index].Samples.Length == 0) return "EMPTY";
            return tracks[index].Muted ? "STOPPED" : "PLAYING";
        }
    }

    private void HandleTrackButton(int trackIndex, bool down)
    {
        if (!down)
        {
            if (heldTrack != trackIndex) return;
            transportHoldCancellation?.Cancel();
            transportHoldCancellation?.Dispose();
            transportHoldCancellation = null;
            heldTrack = -1;
            if (eraseArmedTrack == trackIndex)
            {
                eraseArmedTrack = -1;
                StateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (ignoreTransportRelease)
            {
                ignoreTransportRelease = false;
                return;
            }
            ToggleTrackPlayback(trackIndex);
            return;
        }

        transportHoldCancellation?.Cancel();
        transportHoldCancellation?.Dispose();
        transportHoldCancellation = null;
        heldTrack = trackIndex;
        ignoreTransportRelease = false;

        if (recording && recordingTrack == trackIndex)
        {
            ignoreTransportRelease = true;
            StopRecording();
            return;
        }

        if (recording)
        {
            ignoreTransportRelease = true;
            StopRecording();
            selectedTrack = trackIndex;
            Report($"Track {trackIndex + 1} selected · previous recording is finishing");
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        selectedTrack = trackIndex;
        if (finalizing && recordingTrack == trackIndex)
        {
            ignoreTransportRelease = true;
            Report("Finishing loop · ready in a moment");
            return;
        }
        if (!HasTrackAudio(trackIndex))
        {
            ignoreTransportRelease = true;
            ToggleRecording();
        }
        else
        {
            Report($"Track {trackIndex + 1} selected · {GetTrackSteps(trackIndex)} steps");
            StateChanged?.Invoke(this, EventArgs.Empty);
            transportHoldCancellation = new CancellationTokenSource();
            _ = MonitorEraseHoldAsync(trackIndex, transportHoldCancellation.Token);
        }
    }

    public void ReleaseAll()
    {
        transportHoldCancellation?.Cancel();
        transportHoldCancellation?.Dispose();
        transportHoldCancellation = null;
        heldTrack = -1;
        eraseArmedTrack = -1;
        surfaceActive = false;
        stripActive = false;
        shiftHeld = false;
        shiftChordUsed = false;
        heldPunchEffect = PunchEffect.None;
        dubMode = false;
        SetPunchEffect(PunchEffect.None);
        if (recording)
        {
            StopRecording();
        }
        liveInputEffect = LiveInputEffect.None;
        mixProvider.LiveEffect = LiveInputEffect.None;
        mixProvider.SetLiveInput(null);
        StopCapture();
    }

    private void HandleDubOrHold()
    {
        if (punchEffect != PunchEffect.None)
        {
            heldPunchEffect = heldPunchEffect == punchEffect ? PunchEffect.None : punchEffect;
            if (heldPunchEffect == PunchEffect.None && !surfaceActive) SetPunchEffect(PunchEffect.None);
            Report(heldPunchEffect == PunchEffect.None ? "Punch hold released" : $"Punch hold · {EffectName(heldPunchEffect)}");
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!dubMode)
        {
            if (!HasTrackAudio(selectedTrack)) { Report($"T{selectedTrack + 1} is empty · record the base loop first"); return; }
            dubMode = true;
            ToggleRecording();
        }
        else if (recording)
        {
            StopRecording();
            Report($"Dub finishing · Undo will be ready on T{selectedTrack + 1}");
        }
        else
        {
            dubMode = false;
        }
        Report(dubMode ? $"Dub layer · T{selectedTrack + 1}" : "Dub layer closed");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleShiftTapUndo(bool down)
    {
        if (down)
        {
            shiftHeld = true;
            shiftChordUsed = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!shiftHeld) return;
        shiftHeld = false;
        if (!shiftChordUsed)
        {
            if (dubMode)
            {
                if (CanUndo) Undo();
                else Report("Finish the dub layer before Undo");
            }
            else TapTempo();
        }
        shiftChordUsed = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TapTempo()
    {
        if (recording || finalizing) { Report("Finish recording before changing tempo"); return; }
        manualTempo = true;
        BpmWasDetected = false;
        try { EnsureOutput(); }
        catch (Exception exception) { Report($"Looper output unavailable · {exception.Message}"); }
        var now = DateTime.UtcNow;
        while (tempoTaps.Count > 0 && now - tempoTaps.Peek() > TimeSpan.FromSeconds(3)) tempoTaps.Dequeue();
        tempoTaps.Enqueue(now);
        while (tempoTaps.Count > 6) tempoTaps.Dequeue();
        if (tempoTaps.Count >= 2)
        {
            var taps = tempoTaps.ToArray();
            var intervals = taps.Zip(taps.Skip(1), (a, b) => (b - a).TotalSeconds).OrderBy(value => value).ToArray();
            var median = intervals[intervals.Length / 2];
            if (median is > 0.25 and < 2.0) mixProvider.SetTempo(60d / median);
        }
        mixProvider.AlignBeatToTap(OutputBufferMilliseconds + syncCompensationMilliseconds);
        Report($"Tap · {mixProvider.TempoBpm:0.0} BPM");
        ScheduleSessionSave();
    }

    private void ToggleStripFxRoute()
    {
        int trackIndex;
        if (stripX < 42) trackIndex = stripY >= 64 ? 0 : 1;
        else if (stripX > 86) trackIndex = stripY >= 64 ? 2 : 3;
        else return;

        lock (syncRoot) tracks[trackIndex].FxEnabled = !tracks[trackIndex].FxEnabled;
        Report($"T{trackIndex + 1} punch FX {(IsTrackFxEnabled(trackIndex) ? "ON" : "OFF")}");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CycleLiveInputEffect()
    {
        liveInputEffect = (LiveInputEffect)(((int)liveInputEffect + 1) % 4);
        mixProvider.LiveEffect = liveInputEffect;
        if (liveInputEffect == LiveInputEffect.None)
        {
            mixProvider.SetLiveInput(null);
            if (!recording) StopCapture();
        }
        else
        {
            try
            {
                EnsureOutput();
                EnsureCapture();
            }
            catch (Exception exception)
            {
                liveInputEffect = LiveInputEffect.None;
                mixProvider.LiveEffect = LiveInputEffect.None;
                Report($"Live FX input unavailable · {exception.Message}");
            }
        }
        Report($"Live input FX · {LiveEffectName}");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleRecording()
    {
        if (recording)
        {
            StopRecording();
            return;
        }

        if (finalizing)
        {
            Report("Finishing loop analysis · ready in a moment");
            return;
        }

        try
        {
            EnsureOutput();
            EnsureCapture();
            // Allocate before taking the mixer lock.
            var maximumBytes = (long)(captureFormat!.SampleRate * (MaximumSteps * 15d / mixProvider.TempoBpm)) * captureFormat.BlockAlign;
            maximumBytes = Math.Min(maximumBytes, 128L * 1024 * 1024);
            maximumBytes -= maximumBytes % captureFormat.BlockAlign;
            var stream = new MemoryStream((int)maximumBytes);
            lock (syncRoot)
            {
                maximumRecordingBytes = maximumBytes;
                recordingBytes = stream;
                recordingFormat = captureFormat;
                recordingTrack = selectedTrack;
                recordingSpeed = mixProvider.TempoBpm / mixProvider.ReferenceBpm;
                recordingStartPosition = Math.Max(0, mixProvider.Position - (long)(SampleRate * OutputBufferMilliseconds / 1000d * recordingSpeed));
                recording = true;
                muteRecordingOnFinalize = false;
                automaticStopQueued = false;
                stopAllRequested = false;
            }
            var action = tracks[recordingTrack].Samples.Length == 0 ? "recording" : "overdubbing";
            Report($"T{recordingTrack + 1} {action} · {captureDeviceName} · {recordingFormat?.SampleRate} Hz · {recordingFormat?.Channels} ch");
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            StopCapture();
            lock (syncRoot)
            {
                recording = false;
                recordingBytes?.Dispose();
                recordingBytes = null;
                recordingFormat = null;
            }
            Report($"Looper input unavailable · {exception.Message}");
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void StopRecording(bool discard = false)
    {
        lock (syncRoot)
        {
            if (!recording) return;
            recording = false;
            finalizing = !discard;
            if (discard)
            {
                recordingBytes?.Dispose(); recordingBytes = null; recordingFormat = null;
            }
            else
            {
                finalizationTask = Task.Run(() =>
                {
                    try { FinalizeRecording(); }
                    catch (Exception exception) { Report($"Could not finish loop · {exception.Message}"); }
                    finally
                    {
                        lock (syncRoot) finalizing = false;
                        StateChanged?.Invoke(this, EventArgs.Empty);
                    }
                });
            }
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        if (liveInputEffect == LiveInputEffect.None) StopCapture();
    }

    private void OnCaptureDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!ReferenceEquals(sender, capture)) return;
        liveInputBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
        var shouldStop = false;
        lock (syncRoot)
        {
            if (!recording || recordingBytes is null || recordingFormat is null) return;
            var writable = (int)Math.Min(e.BytesRecorded, Math.Max(0, maximumRecordingBytes - recordingBytes.Length));
            writable -= writable % recordingFormat.BlockAlign;
            if (writable > 0)
            {
                recordingBytes.Write(e.Buffer, 0, writable);
            }
            if (recordingBytes.Length >= maximumRecordingBytes)
            {
                if (!automaticStopQueued)
                {
                    automaticStopQueued = true;
                    shouldStop = true;
                }
            }
        }
        if (shouldStop)
        {
            var owner = recordingBytes;
            _ = Task.Run(() =>
            {
                lock (deviceLock)
                {
                    if (ReferenceEquals(owner, recordingBytes)) StopRecording();
                }
            });
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (!ReferenceEquals(sender, capture)) return;
        // Dispose/join from a worker, never from the capture callback itself.
        _ = Task.Run(() =>
        {
            lock (deviceLock)
            {
                if (!ReferenceEquals(sender, capture)) return;
                if (recording) StopRecording();
                DisposeCapture();
                liveInputEffect = LiveInputEffect.None;
                mixProvider.LiveEffect = LiveInputEffect.None;
                Report(e.Exception is null ? "Input stopped · captured loop preserved" : $"Input stopped · {e.Exception.Message}");
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private void FinalizeRecording()
    {
        MemoryStream? nativeStream;
        WaveFormat? nativeFormat;
        int trackIndex, revision;
        long startPosition;
        bool detectTempo;
        short[] original;
        double reference, speed;
        lock (syncRoot)
        {
            nativeStream = recordingBytes; nativeFormat = recordingFormat;
            recordingBytes = null; recordingFormat = null;
            trackIndex = recordingTrack; startPosition = recordingStartPosition;
            original = tracks[trackIndex].Samples;
            revision = tracks[trackIndex].Revision;
            detectTempo = !manualTempo && !tracks.Any(candidate => candidate.Samples.Length > 0);
            reference = mixProvider.ReferenceBpm;
            speed = recordingSpeed;
        }

        // Published PCM is immutable. Conversion, copies and overdubs run off the audio lock.
        short[] captured;
        using (nativeStream) captured = nativeStream is null || nativeFormat is null ? [] : ConvertToMono48k(nativeStream, nativeFormat);
        if (captured.Length == 0) { Report($"Track {trackIndex + 1} recording was empty"); return; }
        var detectedBpm = 120d;
        var tempoDetected = detectTempo && TryDetectBpm(captured, out detectedBpm);
        if (detectTempo)
        {
            reference = tempoDetected ? detectedBpm : mixProvider.TempoBpm;
            speed = 1;
        }
        // Store all tracks in the reference-tempo timeline, including overdubs at a changed BPM.
        captured = ResampleToTimeline(captured, speed);
        var steps = original.Length == 0 ? Math.Clamp((int)Math.Round(captured.Length / SamplesPerStep(reference)), 1, MaximumSteps) : tracks[trackIndex].Steps;
        var completed = original.Length == 0 ? new short[Math.Max(1, (int)Math.Round(steps * SamplesPerStep(reference)))] : (short[])original.Clone();
        if (original.Length == 0)
        {
            Array.Copy(captured, completed, Math.Min(captured.Length, completed.Length));
            ApplyBoundaryFade(completed);
        }
        else
        {
            var trackStart = tracks[trackIndex].StartPosition;
            for (var i = 0; i < captured.Length; i++)
            {
                var target = PositiveModulo(startPosition + i - trackStart, completed.Length);
                completed[target] = Clip(completed[target] + captured[i]);
            }
        }
        lock (syncRoot)
        {
            var track = tracks[trackIndex];
            if (track.Revision != revision) return; // Clear/undo wins over an in-flight finalization.
            if (detectTempo) { BpmWasDetected = tempoDetected; mixProvider.ConfigureReferenceTempo(reference); }
            track.UndoSamples = original;
            track.Samples = completed;
            track.Steps = steps;
            if (original.Length == 0) track.StartPosition = QuantizeToStep(startPosition, SamplesPerStep(reference));
            track.Muted = muteRecordingOnFinalize || stopAllRequested;
            muteRecordingOnFinalize = false;
            track.Revision++;
            audioRevision++;
        }
        Report(original.Length == 0 ? $"Track {trackIndex + 1} captured · {steps} steps · {mixProvider.TempoBpm:0.0} BPM" : $"Track {trackIndex + 1} overdub captured");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static short[] ResampleToTimeline(short[] source, double speed)
    {
        if (Math.Abs(speed - 1) < 0.000001 || source.Length == 0) return source;
        var result = new short[Math.Max(1, (int)Math.Round(source.Length * speed))];
        for (var i = 0; i < result.Length; i++)
        {
            var position = Math.Min(i / speed, source.Length - 1);
            var left = (int)position;
            result[i] = (short)Math.Round(source[left] + (source[Math.Min(left + 1, source.Length - 1)] - source[left]) * (position - left));
        }
        return result;
    }

    private void ToggleTrackPlayback(int trackIndex)
    {
        lock (syncRoot)
        {
            var track = tracks[trackIndex];
            if (track.Samples.Length == 0)
            {
                Report($"Track {trackIndex + 1} is empty");
                return;
            }
            track.Muted = !track.Muted;
            if (!track.Muted) stopAllRequested = false;
            Report($"Track {trackIndex + 1} {(track.Muted ? "stopped" : "playing")}");
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PlayAll()
    {
        lock (syncRoot)
        {
            foreach (var track in tracks.Where(track => track.Samples.Length > 0)) track.Muted = false;
            stopAllRequested = false;
        }
        Report("Play all tracks");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StopAll()
    {
        lock (syncRoot) stopAllRequested = true;
        if (recording)
        {
            lock (syncRoot) muteRecordingOnFinalize = true;
            StopRecording();
        }
        lock (syncRoot)
        {
            foreach (var track in tracks) track.Muted = true;
        }
        Report("Stop all tracks");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Undo()
    {
        lock (syncRoot)
        {
            if (recording || finalizing) { Report("Finish the dub layer before Undo"); return; }
            var track = tracks[selectedTrack];
            if (track.UndoSamples.Length == 0)
            {
                Report($"Track {selectedTrack + 1} has no overdub to undo");
                return;
            }
            (track.Samples, track.UndoSamples) = (track.UndoSamples, track.Samples);
            track.Revision++;
            audioRevision++;
            Report($"Track {selectedTrack + 1} undo");
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearTrack(int trackIndex)
    {
        if (recording && recordingTrack == trackIndex) StopRecording(discard: true);
        lock (syncRoot)
        {
            tracks[trackIndex].Revision++;
            tracks[trackIndex].Samples = [];
            tracks[trackIndex].UndoSamples = [];
            tracks[trackIndex].Muted = false;
            tracks[trackIndex].Steps = 0;
            tracks[trackIndex].StartPosition = 0;
            audioRevision++;
        }
        Report($"Track {trackIndex + 1} erased");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task MonitorEraseHoldAsync(int trackIndex, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(550, cancellationToken);
            eraseArmedTrack = trackIndex;
            Report($"Hold track {trackIndex + 1} · erasing…");
            StateChanged?.Invoke(this, EventArgs.Empty);
            await Task.Delay(450, cancellationToken);
            if (heldTrack == trackIndex)
            {
                ignoreTransportRelease = true;
                eraseArmedTrack = -1;
                ClearTrack(trackIndex);
            }
        }
        catch (OperationCanceledException)
        {
            if (eraseArmedTrack == trackIndex)
            {
                eraseArmedTrack = -1;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void SetPunchEffect(PunchEffect effect)
    {
        if (punchEffect == effect) return;
        punchEffect = effect;
        mixProvider.BeginEffect(effect);
        if (effect != PunchEffect.None)
        {
            Report($"Track {selectedTrack + 1} punch · {EffectName(effect)}");
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureOutput()
    {
        if (output is not null) { output.Play(); return; }
        MMDevice? outputDevice = null;
        if (!string.IsNullOrWhiteSpace(configuredOutputDeviceId))
        {
            using var enumerator = new MMDeviceEnumerator();
            try { outputDevice = enumerator.GetDevice(configuredOutputDeviceId); }
            catch { configuredOutputDeviceId = null; }
        }
        output = outputDevice is null
            ? new WasapiOut(AudioClientShareMode.Shared, true, OutputBufferMilliseconds)
            : new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, OutputBufferMilliseconds);
        try { output.Init(mixProvider); output.Play(); }
        catch { output.Dispose(); output = null; throw; }
        finally { outputDevice?.Dispose(); }
    }

    private void EnsureCapture()
    {
        lock (deviceLock)
        {
            if (capture is not null) return;
            using var enumerator = new MMDeviceEnumerator();
            MMDevice inputDevice;
            try
            {
                inputDevice = string.IsNullOrWhiteSpace(configuredInputDeviceId)
                    ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console)
                    : enumerator.GetDevice(configuredInputDeviceId);
            }
            catch
            {
                configuredInputDeviceId = null;
                inputDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            }
            captureDeviceName = inputDevice.FriendlyName;
            capture = new WasapiCapture(inputDevice);
            captureFormat = capture.WaveFormat;
            liveInputBuffer = new BufferedWaveProvider(captureFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(500),
                DiscardOnBufferOverflow = true,
                ReadFully = true,
            };
            ISampleProvider liveSamples = liveInputBuffer.ToSampleProvider();
            if (liveSamples.WaveFormat.Channels > 1) liveSamples = new FirstTwoChannelMonoSampleProvider(liveSamples);
            if (liveSamples.WaveFormat.SampleRate != SampleRate) liveSamples = new WdlResamplingSampleProvider(liveSamples, SampleRate);
            mixProvider.SetLiveInput(liveSamples);
            capture.DataAvailable += OnCaptureDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            try { capture.StartRecording(); }
            catch { DisposeCapture(); throw; }
            finally { inputDevice.Dispose(); }
        }

    }

    private void StopCapture() => DisposeCapture();

    private void DisposeCapture()
    {
        lock (deviceLock)
        {
            var oldCapture = capture;
            capture = null;
            if (oldCapture is null) return;
            oldCapture.DataAvailable -= OnCaptureDataAvailable;
            oldCapture.RecordingStopped -= OnRecordingStopped;
            oldCapture.Dispose();
            liveInputBuffer = null;
            captureFormat = null;

            mixProvider.SetLiveInput(null);
        }

    }

    private void ScheduleSessionSave()
    {
        CancellationToken token;
        lock (syncRoot)
        {
            if (disposed) return;
            sessionSaveCancellation?.Cancel();
            sessionSaveCancellation?.Dispose();
            sessionSaveCancellation = new CancellationTokenSource();
            token = sessionSaveCancellation.Token;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(650, token);
                SaveSessionSnapshot();
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private void SaveSessionSnapshot(bool forceAudio = false)
    {
        lock (saveLock)
        {
            try
            {
                SessionState state;
                var generation = Guid.NewGuid().ToString("N");
                short[][]? audio = null;
                int revision;
                lock (syncRoot)
                {
                    revision = audioRevision;
                    var saveAudio = forceAudio || revision != savedAudioRevision;
                    if (saveAudio) audio = tracks.Select(track => track.Samples).ToArray();
                    state = new SessionState
                    {
                        Version = 1,
                        ManualTempo = manualTempo,
                        SelectedTrack = selectedTrack,
                        TempoBpm = mixProvider.TempoBpm,
                        ReferenceBpm = mixProvider.ReferenceBpm,
                        Position = mixProvider.Position,
                        MasterVolume = mixProvider.MasterVolume,
                        EffectDepth = mixProvider.EffectDepth,
                        LiveEffectDepth = mixProvider.LiveEffectDepth,
                        Tracks = tracks.Select((track, index) => new SessionTrackState
                        {
                            File = saveAudio ? $"track-{index + 1}-{generation}.pcm" : track.SavedFile,
                            Steps = track.Steps,
                            StartPosition = track.StartPosition,
                            Muted = track.Muted,
                            FxEnabled = track.FxEnabled,
                        }).ToArray(),
                    };
                }

                Directory.CreateDirectory(sessionDirectory);
                if (audio is not null)
                {
                    for (var index = 0; index < audio.Length; index++)
                    {
                        using var file = File.Create(Path.Combine(sessionDirectory, state.Tracks[index].File));
                        file.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(audio[index].AsSpan()));
                        file.Flush(flushToDisk: true);
                    }
                }
                var temporary = sessionStatePath + ".tmp";
                File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(state,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, sessionStatePath, overwrite: true);
                lock (syncRoot)
                {
                    savedAudioRevision = revision;
                    for (var i = 0; i < tracks.Length; i++) tracks[i].SavedFile = state.Tracks[i].File;
                }
                // Only remove generations after their replacement manifest has committed.
                var retained = state.Tracks.Select(t => t.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var path in Directory.EnumerateFiles(sessionDirectory, "track-*.pcm"))
                    if (!retained.Contains(Path.GetFileName(path))) File.Delete(path);
            }
            catch
            {
                // The active in-memory session remains usable if local persistence is unavailable.
            }
        }

    }

    private void LoadSession()
    {
        try
        {
            if (!File.Exists(sessionStatePath)) return;
            var state = System.Text.Json.JsonSerializer.Deserialize<SessionState>(File.ReadAllText(sessionStatePath));
            if (state?.Version != 1 || state.Tracks is null || state.Tracks.Length != tracks.Length) return;
            if (!double.IsFinite(state.TempoBpm) || !double.IsFinite(state.ReferenceBpm) ||
                !float.IsFinite(state.MasterVolume) || !float.IsFinite(state.EffectDepth) || !float.IsFinite(state.LiveEffectDepth)) return;
            // Validate every entry before installing any track.
            foreach (var entry in state.Tracks)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.File) || entry.File != Path.GetFileName(entry.File) || entry.File.Contains(':')) return;
                var file = new FileInfo(Path.Combine(sessionDirectory, entry.File));
                if (!file.Exists || file.Length % 2 != 0 || file.Length > SampleRate * 128L * 2) return;
            }
            manualTempo = state.ManualTempo;
            for (var index = 0; index < tracks.Length; index++)
            {
                var savedTrack = state.Tracks[index];
                var audioPath = Path.Combine(sessionDirectory, savedTrack.File);
                var bytes = File.Exists(audioPath) ? File.ReadAllBytes(audioPath) : [];
                var samples = new short[bytes.Length / sizeof(short)];
                if (samples.Length > 0) Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * sizeof(short));
                tracks[index].Samples = samples;
                tracks[index].SavedFile = savedTrack.File;
                tracks[index].Steps = samples.Length == 0 ? 0 : Math.Clamp(savedTrack.Steps, 1, MaximumSteps);
                tracks[index].StartPosition = savedTrack.StartPosition;
                tracks[index].Muted = savedTrack.Muted;
                tracks[index].FxEnabled = savedTrack.FxEnabled;
            }
            selectedTrack = Math.Clamp(state.SelectedTrack, 0, tracks.Length - 1);
            mixProvider.RestoreState(state.ReferenceBpm, state.TempoBpm, state.Position,
                state.MasterVolume, state.EffectDepth, state.LiveEffectDepth);
            audioRevision = savedAudioRevision = 1;
        }
        catch
        {
            foreach (var track in tracks) { track.Samples = []; track.Steps = 0; }
            // A damaged session starts empty rather than preventing app startup.
        }
    }

    private void Report(string message) => ActionReported?.Invoke(this, message);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        sessionSaveCancellation?.Cancel();
        sessionSaveCancellation?.Dispose();
        sessionSaveCancellation = null;
        mixProvider.SetActive(false);
        ReleaseAll();
        finalizationTask.GetAwaiter().GetResult();
        DisposeCapture();
        output?.Stop();
        output?.Dispose();
        output = null;
        SaveSessionSnapshot(forceAudio: true);
    }

    private static short Clip(int sample) => (short)Math.Clamp(sample, short.MinValue, short.MaxValue);

    private static long QuantizeToStep(long position, double stepSamples) =>
        (long)Math.Round(Math.Round(position / stepSamples) * stepSamples);

    private static int PositiveModulo(long value, int modulus) => (int)((value % modulus + modulus) % modulus);

    private static void ApplyBoundaryFade(short[] samples)
    {
        var fadeLength = Math.Min(samples.Length / 4, SampleRate / 250); // 4 ms prevents boundary clicks without dulling speech.
        for (var index = 0; index < fadeLength; index++)
        {
            var gain = index / (float)Math.Max(1, fadeLength);
            samples[index] = (short)(samples[index] * gain);
            samples[^(index + 1)] = (short)(samples[^(index + 1)] * gain);
        }
    }

    private static short[] ConvertToMono48k(Stream nativeBytes, WaveFormat nativeFormat)
    {
        if (nativeBytes.Length == 0) return [];
        nativeBytes.Position = 0;
        using var stream = new RawSourceWaveStream(nativeBytes, nativeFormat);
        ISampleProvider samples = stream.ToSampleProvider();
        if (samples.WaveFormat.Channels > 1)
        {
            samples = new FirstTwoChannelMonoSampleProvider(samples);
        }
        if (samples.WaveFormat.SampleRate != SampleRate)
        {
            samples = new WdlResamplingSampleProvider(samples, SampleRate);
        }

        var converted = new List<short>((int)Math.Ceiling(nativeBytes.Length / (double)nativeFormat.BlockAlign * SampleRate / nativeFormat.SampleRate));
        var buffer = new float[4096];
        int read;
        while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var index = 0; index < read; index++)
            {
                converted.Add(Clip((int)Math.Round(Math.Clamp(buffer[index], -1f, 1f) * short.MaxValue)));
            }
        }
        return converted.ToArray();
    }

    private static double SamplesPerStep(double bpm) => SampleRate * 60d / Math.Clamp(bpm, 60d, 180d) / 4d;

    private static bool TryDetectBpm(short[] samples, out double bpm)
    {
        bpm = 120d;
        const int window = 1024;
        const int hop = 512;
        if (samples.Length < SampleRate * 2) return false;

        var energies = new List<double>();
        for (var start = 0; start + window < samples.Length; start += hop)
        {
            double energy = 0;
            for (var index = start; index < start + window; index++)
            {
                var normalized = samples[index] / 32768d;
                energy += normalized * normalized;
            }
            energies.Add(energy / window);
        }

        if (energies.Count < 4) return false;
        var mean = energies.Average();
        var threshold = mean * 1.55;
        var minimumPeakDistance = Math.Max(1, (int)(SampleRate / (hop * 5d)));
        var peaks = new List<int>();
        for (var index = 1; index < energies.Count - 1; index++)
        {
            if (energies[index] > threshold && energies[index] >= energies[index - 1] && energies[index] > energies[index + 1] &&
                (peaks.Count == 0 || index - peaks[^1] >= minimumPeakDistance))
            {
                peaks.Add(index);
            }
        }

        if (peaks.Count < 3) return TryAutocorrelationBpm(energies, hop, samples.Length, out bpm);
        var intervals = peaks.Zip(peaks.Skip(1), (left, right) => (right - left) * hop / (double)SampleRate)
            .Where(seconds => seconds is > 0.2 and < 2.0)
            .OrderBy(seconds => seconds)
            .ToArray();
        if (intervals.Length < 2) return TryAutocorrelationBpm(energies, hop, samples.Length, out bpm);
        var median = intervals[intervals.Length / 2];
        bpm = 60d / median;
        while (bpm < 70d) bpm *= 2d;
        while (bpm > 180d) bpm /= 2d;
        bpm = Math.Clamp(bpm, 60d, 180d);
        return true;
    }

    private static bool TryAutocorrelationBpm(IReadOnlyList<double> energies, int hop, int sampleCount, out double bpm)
    {
        bpm = 120d;
        if (energies.Count < 16) return false;
        var novelty = new double[energies.Count];
        for (var index = 1; index < energies.Count; index++) novelty[index] = Math.Max(0d, energies[index] - energies[index - 1]);
        var noveltyPower = novelty.Sum(value => value * value);
        if (noveltyPower < 1e-10) return false;

        var duration = sampleCount / (double)SampleRate;
        var scores = new List<(double Bpm, double Score)>();
        for (var candidate = 60d; candidate <= 180d; candidate += 0.5d)
        {
            var lag = Math.Max(1, (int)Math.Round(60d / candidate * SampleRate / hop));
            double correlation = 0;
            for (var index = lag; index < novelty.Length; index++) correlation += novelty[index] * novelty[index - lag];
            var steps = duration * candidate / 15d;
            var gridCloseness = 1d - Math.Min(1d, Math.Abs(steps - Math.Round(steps)) * 2d);
            var barBonus = Math.Abs(Math.Round(steps) % 16) < 0.01 ? 1.12d : Math.Abs(Math.Round(steps) % 4) < 0.01 ? 1.05d : 1d;
            scores.Add((candidate, correlation * (0.8d + (gridCloseness * 0.2d)) * barBonus));
        }
        var ordered = scores.OrderByDescending(item => item.Score).ToArray();
        var average = scores.Average(item => item.Score);
        if (ordered[0].Score <= 0 || ordered[0].Score < average * 1.08d) return false;
        bpm = ordered[0].Bpm;
        return true;
    }

    private static string EffectName(PunchEffect effect) => effect switch
    {
        PunchEffect.Stutter => "STUTTER",
        PunchEffect.Reverse => "REVERSE",
        PunchEffect.HalfSpeed => "HALF SPEED",
        PunchEffect.Gate => "GATE",
        _ => "DRY",
    };

    internal sealed class LoopTrack
    {
        public int Revision { get; set; }
        public string SavedFile { get; set; } = "";
        public short[] Samples { get; set; } = [];
        public short[] UndoSamples { get; set; } = [];
        public bool Muted { get; set; }
        public int Steps { get; set; }
        public long StartPosition { get; set; }
        public bool FxEnabled { get; set; } = true;
    }

    private sealed class SessionState
    {
        public int Version { get; set; }
        public bool ManualTempo { get; set; }
        public int SelectedTrack { get; set; }
        public double TempoBpm { get; set; } = 120d;
        public double ReferenceBpm { get; set; } = 120d;
        public long Position { get; set; }
        public float MasterVolume { get; set; } = 1f;
        public float EffectDepth { get; set; } = 1f;
        public float LiveEffectDepth { get; set; } = 0.65f;
        public SessionTrackState[] Tracks { get; set; } = [];
    }

    private sealed class SessionTrackState
    {
        public string File { get; set; } = "";
        public int Steps { get; set; }
        public long StartPosition { get; set; }
        public bool Muted { get; set; }
        public bool FxEnabled { get; set; } = true;
    }

    private sealed class FirstTwoChannelMonoSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private float[] sourceBuffer = [];

        public FirstTwoChannelMonoSampleProvider(ISampleProvider source)
        {
            this.source = source;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var channels = source.WaveFormat.Channels;
            var required = count * channels;
            if (sourceBuffer.Length < required) sourceBuffer = new float[required];
            var sourceRead = source.Read(sourceBuffer, 0, required);
            var frames = sourceRead / channels;
            for (var frame = 0; frame < frames; frame++)
            {
                var sourceOffset = frame * channels;
                if (channels == 1)
                {
                    buffer[offset + frame] = sourceBuffer[sourceOffset];
                    continue;
                }
                var left = sourceBuffer[sourceOffset];
                var right = sourceBuffer[sourceOffset + 1];
                var leftMagnitude = Math.Abs(left);
                var rightMagnitude = Math.Abs(right);
                buffer[offset + frame] = rightMagnitude < leftMagnitude * 0.25f
                    ? left
                    : leftMagnitude < rightMagnitude * 0.25f
                        ? right
                        : left * right < 0 && leftMagnitude is > 0.0001f && rightMagnitude is > 0.0001f
                            ? left
                            : (left + right) * 0.5f;
            }
            return frames;
        }
    }

    internal enum PunchEffect
    {
        None,
        Stutter,
        Reverse,
        HalfSpeed,
        Gate,
    }

    internal enum LiveInputEffect
    {
        None,
        Reverb,
        DubEcho,
        Robot,
    }

    internal sealed class LoopMixProvider(
        object syncRoot,
        LoopTrack[] tracks,
        Func<PunchEffect> currentEffect) : IWaveProvider
    {
        private double position;
        private long effectStart;
        private double tempoBpm = 120d;
        private double referenceBpm = 120d;
        private ISampleProvider? liveInput;
        private float[] liveBuffer = new float[SampleRate];
        private readonly float[] reverbDelay = new float[SampleRate * 2];
        private readonly float[] echoDelay = new float[SampleRate];
        private int reverbIndex;
        private int echoIndex;
        private long liveEffectPosition;
        private bool active;
        public float InputPeak { get; private set; }

        public WaveFormat WaveFormat { get; } = new(SampleRate, 16, 2);
        public long Position
        {
            get
            {
                lock (syncRoot) return (long)position;
            }
        }
        public double TempoBpm
        {
            get
            {
                lock (syncRoot) return tempoBpm;
            }
        }
        public double ReferenceBpm
        {
            get
            {
                lock (syncRoot) return referenceBpm;
            }
        }
        public double BeatPhase
        {
            get
            {
                lock (syncRoot)
                {
                    var beatSamples = SampleRate * 60d / referenceBpm;
                    return (position % beatSamples) / beatSamples;
                }
            }
        }
        public float MasterVolume { get; set; } = 1f;
        public float EffectDepth { get; set; } = 1f;
        public float LiveEffectDepth { get; set; } = 0.65f;
        public LiveInputEffect LiveEffect { get; set; }

        public void ConfigureReferenceTempo(double bpm)
        {
            lock (syncRoot)
            {
                referenceBpm = tempoBpm = Math.Clamp(bpm, 60d, 180d);
            }
        }

        public void RestoreState(double reference, double tempo, long restoredPosition, float volume, float punchDepth, float liveDepth)
        {
            lock (syncRoot)
            {
                referenceBpm = Math.Clamp(reference, 60d, 180d);
                tempoBpm = Math.Clamp(tempo, 60d, 180d);
                position = Math.Max(0, restoredPosition);
                MasterVolume = Math.Clamp(volume, 0f, 1.2f);
                EffectDepth = Math.Clamp(punchDepth, 0f, 1f);
                LiveEffectDepth = Math.Clamp(liveDepth, 0f, 1f);
            }
        }

        public void SetTempo(double bpm)
        {
            lock (syncRoot) tempoBpm = Math.Clamp(bpm, 60d, 180d);
        }

        public void SetActive(bool value)
        {
            lock (syncRoot) active = value;
        }

        public void AlignBeatToTap(int latencyMilliseconds)
        {
            lock (syncRoot)
            {
                var beatSamples = SampleRate * 60d / referenceBpm;
                var latencySamples = SampleRate * Math.Clamp(latencyMilliseconds, -190, 310) / 1000d * tempoBpm / referenceBpm;
                position = Math.Round(position / beatSamples) * beatSamples + latencySamples;
                if (position < 0) position += beatSamples;
            }
        }

        public void BeginEffect(PunchEffect effect)
        {
            if (effect != PunchEffect.None) effectStart = Position;
        }

        public void SetLiveInput(ISampleProvider? input)
        {
            lock (syncRoot)
            {
                liveInput = input;
                if (input is null) { Array.Clear(reverbDelay); Array.Clear(echoDelay); InputPeak = 0; }
            }
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            var frameCount = count / 4;
            lock (syncRoot)
            {
                if (!active)
                {
                    Array.Clear(buffer, offset, count);
                    return count;
                }
                if (liveBuffer.Length < frameCount) liveBuffer = new float[frameCount];
                Array.Clear(liveBuffer, 0, frameCount);
                liveInput?.Read(liveBuffer, 0, frameCount);
                var peak = 0f;
                for (var i = 0; i < frameCount; i++) peak = Math.Max(peak, Math.Abs(liveBuffer[i]));
                InputPeak = Math.Min(1, peak);
                var effect = currentEffect();
                var speed = tempoBpm / referenceBpm;
                for (var frameOffset = 0; frameOffset < frameCount; frameOffset++)
                {
                    var mixed = 0f;
                    var currentPosition = position;
                    position += speed;
                    for (var trackIndex = 0; trackIndex < tracks.Length; trackIndex++)
                    {
                        var track = tracks[trackIndex];
                        if (track.Muted || track.Samples.Length == 0) continue;
                        var dry = Interpolate(track.Samples, currentPosition - track.StartPosition);
                        var effected = dry;
                        if (track.FxEnabled)
                        {
                            effected = GetEffectedSample(track, currentPosition, effect, dry);
                        }
                        mixed += dry + ((effected - dry) * EffectDepth);
                    }
                    if (LiveEffect != LiveInputEffect.None)
                    {
                        mixed += ProcessLiveEffect(liveBuffer[frameOffset], LiveEffect) * short.MaxValue * LiveEffectDepth;
                    }
                    var normalized = mixed / short.MaxValue * MasterVolume;
                    var outputSample = Clip((int)(Math.Tanh(normalized) * short.MaxValue));
                    var byteOffset = offset + (frameOffset * 4);
                    buffer[byteOffset] = (byte)(outputSample & 0xFF);
                    buffer[byteOffset + 1] = (byte)((outputSample >> 8) & 0xFF);
                    buffer[byteOffset + 2] = buffer[byteOffset];
                    buffer[byteOffset + 3] = buffer[byteOffset + 1];
                }
            }
            Array.Clear(buffer, offset + (frameCount * 4), count - (frameCount * 4));
            return count;
        }

        internal static float Interpolate(short[] samples, double position)
        {
            var floor = (long)Math.Floor(position);
            var index = PositiveModulo(floor, samples.Length);
            var next = index + 1 == samples.Length ? 0 : index + 1;
            return samples[index] + (samples[next] - samples[index]) * (float)(position - floor);
        }

        private float GetEffectedSample(LoopTrack track, double currentPosition, PunchEffect effect, float dry)
        {
            if (effect == PunchEffect.None) return dry;
            var length = track.Samples.Length;
            var elapsed = Math.Max(0, currentPosition - effectStart);
            float At(double absolutePosition) => Interpolate(track.Samples, absolutePosition - track.StartPosition);
            return effect switch
            {
                PunchEffect.Stutter => At(effectStart + (elapsed % (SampleRate / 8))),
                PunchEffect.Reverse => At(effectStart - elapsed),
                PunchEffect.HalfSpeed => At(effectStart + (elapsed / 2)),
                PunchEffect.Gate => ((long)(elapsed / (SampleRate / 16))) % 2 == 0 ? dry : (short)0,
                _ => dry,
            };
        }

        private float ProcessLiveEffect(float input, LiveInputEffect effect)
        {
            liveEffectPosition++;
            switch (effect)
            {
                case LiveInputEffect.Reverb:
                    var tap1 = reverbDelay[(reverbIndex + reverbDelay.Length - (int)(SampleRate * 0.083)) % reverbDelay.Length];
                    var tap2 = reverbDelay[(reverbIndex + reverbDelay.Length - (int)(SampleRate * 0.149)) % reverbDelay.Length];
                    var tap3 = reverbDelay[(reverbIndex + reverbDelay.Length - (int)(SampleRate * 0.227)) % reverbDelay.Length];
                    var wet = (tap1 * 0.45f) + (tap2 * 0.32f) + (tap3 * 0.23f);
                    reverbDelay[reverbIndex] = input + (wet * 0.48f);
                    reverbIndex = (reverbIndex + 1) % reverbDelay.Length;
                    return wet;
                case LiveInputEffect.DubEcho:
                    var echoSamples = Math.Max(1, (int)(SampleRate * 60d / tempoBpm * 0.75));
                    var readIndex = (echoIndex + echoDelay.Length - Math.Min(echoSamples, echoDelay.Length - 1)) % echoDelay.Length;
                    var echo = echoDelay[readIndex];
                    echoDelay[echoIndex] = input + (echo * 0.58f);
                    echoIndex = (echoIndex + 1) % echoDelay.Length;
                    return echo;
                case LiveInputEffect.Robot:
                    return input * (float)Math.Sin(liveEffectPosition * Math.Tau * 38d / SampleRate);
                default:
                    return 0f;
            }
        }
    }
}
