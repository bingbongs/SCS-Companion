using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using NAudio.Wave;
using SCSCompanion.Models;
using SCSCompanion.Services;
using static SCSCompanion.Services.AudioLooperService;

var root = Path.Combine(Path.GetTempPath(), "SCSCompanion-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    checks++;
    Console.WriteLine("PASS " + name);
}
object? Field(object o, string name) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o);
void Set(object o, string name, object? value) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, value);
object? Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args);
MidiActivity Note(int note, bool down = true) => new(DateTimeOffset.Now, down ? "Note on" : "Note off", 1, note, down ? 127 : 0, []);
string Folder(string name) { var path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }

try
{
    Check(KeyboardOutput.InputSize == (IntPtr.Size == 8 ? 40 : 28), "Windows INPUT ABI size");
    Check(MidiActivity.FromRaw([0x90, 0x30, 0]).Kind == "Note off", "Zero-velocity note-on is a release");
    Check(CustomMappingService.TryParseChord("Ctrl+Shift+K", out var key, out var mods) && key == 0x4B && mods.SequenceEqual(new ushort[] { 0x11, 0x10 }), "Custom shortcut parser");
    Check(!CustomMappingService.TryParseChord("Ctrl+Ctrl+K", out _, out _) && !CustomMappingService.TryParseChord("Ctrl+", out _, out _) && !CustomMappingService.TryParseChord("F25", out _, out _), "Invalid chords rejected");
    var sends = 0;
    var mappingPath = Path.Combine(root, "custom.json");
    var custom = new CustomMappingService(mappingPath, (_, _) => { sends++; return true; });
    Check(custom.SetChord(1, 0, "Alt+F13", out _), "Custom mapping save");
    custom.Handle(Note(0x2C), true, 1); custom.Handle(Note(0x2C), true, 1);
    custom.Handle(Note(0x2C, false), true, 1); custom.Handle(Note(0x2C), true, 1);
    custom.Handle(Note(0x2E), false, 1);
    Check(sends == 2, "One custom action per physical press; paused output suppressed");
    Check(new CustomMappingService(mappingPath).GetChord(1, 0) == "Alt+F13", "Custom mapping reload");

    Check(ResampleToTimeline(new short[] { 0, 1000, 2000, 3000 }, 2).Length == 8, "Faster tempo recording expands into reference timeline");
    Check(ResampleToTimeline(new short[] { 0, 1000, 2000, 3000 }, .5).SequenceEqual(new short[] { 0, 2000 }), "Slower tempo recording contracts into reference timeline");
    Check(LoopMixProvider.Interpolate(new short[] { 0, 1000 }, .5) == 500 && LoopMixProvider.Interpolate(new short[] { 0, 1000 }, 1.5) == 500, "Fractional playback interpolates across loop seam");
    var tracks = Enumerable.Range(0, 4).Select(_ => new LoopTrack()).ToArray();
    tracks[0].Samples = [1000, 2000, 3000]; tracks[1].Samples = [500, -500];
    var fx = PunchEffect.None;
    var mixer = new LoopMixProvider(new object(), tracks, () => fx);
    var bytes = new byte[24];
    mixer.SetActive(true); mixer.Read(bytes, 0, bytes.Length);
    Check(mixer.Position == 6, "Transport advances once per stereo frame");
    Check(BitConverter.ToInt16(bytes, 0) == BitConverter.ToInt16(bytes, 2), "Stereo output channels match");
    Check(BitConverter.ToInt16(bytes, 0) == BitConverter.ToInt16(bytes, 4) && BitConverter.ToInt16(bytes, 8) > BitConverter.ToInt16(bytes, 4), "Independent loop lengths wrap correctly");
    mixer.SetActive(false); mixer.Read(bytes, 0, bytes.Length);
    Check(bytes.All(b => b == 0) && mixer.Position == 6, "Paused mixer is silent and freezes phase");
    mixer.SetActive(true); mixer.SetTempo(180); mixer.Read(bytes, 0, 16);
    Check(mixer.Position == 12, "Tempo ratio changes transport rate");
    mixer.RestoreState(120, 120, 0, 1, 1, .65f); fx = PunchEffect.Reverse; mixer.BeginEffect(fx);
    mixer.Read(bytes, 0, bytes.Length);
    Check(bytes.Any(b => b != 0), "Reverse punch renders audio");

    // Seed capture directly: these checks never open the user's microphone/output.
    var storage = Folder("session");
    var looper = new AudioLooperService(storageDirectory: storage);
    looper.Handle(Note(0x2C), true);
    Check(!looper.IsDubMode, "Empty track does not enter unusable dub mode");
    looper.Handle(Note(0x30), true);
    looper.Handle(Note(0x01), true);
    looper.Handle(new(DateTimeOffset.Now, "Control", 1, 0x01, 90, []), true);
    looper.Handle(Note(0x30, false), true);
    Check(((Queue<DateTime>)Field(looper, "tempoTaps")!).Count == 0, "SHIFT depth adjustment does not accidentally tap tempo");
    looper.Handle(Note(0x01, false), true);
    var stepSize = 48000 * 15d / 127;
    var quantize = typeof(AudioLooperService).GetMethod("QuantizeToStep", BindingFlags.Static | BindingFlags.NonPublic)!;
    var quantized = (long)quantize.Invoke(null, new object[] { (long)Math.Round(stepSize * 500), stepSize })!;
    Check(Math.Abs(quantized - stepSize * 500) <= .5, "Step quantization does not accumulate rounding drift");
    var serviceTracks = (LoopTrack[])Field(looper, "tracks")!;
    serviceTracks[0].Samples = new short[12000]; serviceTracks[0].Steps = 2;
    var original = serviceTracks[0].Samples;
    var captured = Enumerable.Repeat((short)1000, 6000).ToArray();
    var raw = new byte[captured.Length * 2]; Buffer.BlockCopy(captured, 0, raw, 0, raw.Length);
    Set(looper, "recordingBytes", new MemoryStream(raw)); Set(looper, "recordingFormat", new WaveFormat(48000, 16, 1));
    Set(looper, "recordingTrack", 0); Set(looper, "recordingStartPosition", 0L); Set(looper, "recordingSpeed", 2d);
    Call(looper, "FinalizeRecording");
    Check(serviceTracks[0].Samples.All(s => s == 1000) && original.All(s => s == 0), "Overdub at changed tempo fills reference loop without mutating playing PCM");
    Call(looper, "Undo"); Check(serviceTracks[0].Samples.All(s => s == 0), "Undo restores base loop");
    Call(looper, "Undo"); Check(serviceTracks[0].Samples.All(s => s == 1000), "Second undo restores overdub");
    Call(looper, "SaveSessionSnapshot", true);
    Parallel.For(0, 4, _ => Call(looper, "SaveSessionSnapshot", true));
    var manifest = File.ReadAllText(Path.Combine(storage, "session.json"));
    Check(manifest.Contains("track-1-") && Directory.GetFiles(storage, "*.pcm").Length == 4, "Session uses committed generation files");
    using (var state = JsonDocument.Parse(manifest))
        Check(state.RootElement.GetProperty("Tracks").EnumerateArray().All(t => File.Exists(Path.Combine(storage, t.GetProperty("File").GetString()!))), "Concurrent saves leave a complete manifest generation");
    using (var loaded = new AudioLooperService(storageDirectory: storage))
        Check(loaded.HasTrackAudio(0) && loaded.GetTrackSteps(0) == 2, "Session audio and metadata round trip");
    // Shutdown must await finalization before its last save.
    Set(looper, "recordingBytes", new MemoryStream(raw)); Set(looper, "recordingFormat", new WaveFormat(48000, 16, 1));
    Set(looper, "recordingSpeed", 1d); Set(looper, "recording", true);
    looper.Dispose();
    using (var loaded = new AudioLooperService(storageDirectory: storage))
    {
        var restored = (LoopTrack[])Field(loaded, "tracks")!;
        Check(restored[0].Samples[100] == 2000, "Shutdown preserves last active overdub");
    }
    var damaged = Folder("damaged");
    File.WriteAllText(Path.Combine(damaged, "session.json"), "{\"Version\":1,\"Tracks\":null}");
    using (var loaded = new AudioLooperService(storageDirectory: damaged)) Check(!loaded.HasTrackAudio(0), "Malformed session fails safely");
    var escaped = Folder("escaped");
    File.WriteAllText(Path.Combine(escaped, "session.json"), manifest.Replace("track-1-", "../track-1-"));
    using (var loaded = new AudioLooperService(storageDirectory: escaped)) Check(!loaded.HasTrackAudio(0), "Session rejects paths outside its directory");

    var wavePath = Path.Combine(root, "right-only-44100.wav");
    using (var writer = new WaveFileWriter(wavePath, new WaveFormat(44100, 16, 2)))
    {
        for (var i = 0; i < 44100; i++) { writer.WriteSample(0); writer.WriteSample((float)(Math.Sin(i * Math.Tau * 440 / 44100) * .5)); }
    }
    var decoded = KaossPerformanceService.DecodeMono(wavePath);
    Check(Math.Abs(decoded.Length - 48000) < 10, "44.1 kHz sample retains one-second duration at 48 kHz");
    Check(decoded.Max(Math.Abs) > .2f, "Kaoss imports right-channel audio");
    var kaoss = new KaossWaveProvider { Active = true, Gate = true, Program = 5 };
    kaoss.SetSample(decoded); var floats = new float[960]; kaoss.Read(floats, 0, floats.Length);
    Check(floats.All(float.IsFinite) && floats.Any(s => s != 0), "Kaoss sample render is finite and audible");

    using (var mouse = new MouseTrackpadEngine())
    {
        Set(mouse, "playbackCancellation", new CancellationTokenSource());
        Set(mouse, "<IsMacroPlaying>k__BackingField", true);
        Set(mouse, "<IsMacroRecording>k__BackingField", true);
        var cancellation = (CancellationTokenSource)Field(mouse, "playbackCancellation")!;
        mouse.ReleaseAll();
        Check(cancellation.IsCancellationRequested && !mouse.IsMacroPlaying && !mouse.IsMacroRecording, "Safe Stop cancels macro playback and recording");
        cancellation.Dispose();
    }
    var simon = new SimonGameService(0); var lateFlashes = 0;
    simon.Start(); await Task.Delay(190); simon.Stop();
    simon.StateChanged += (_, state) => { if (state.Phase == "flash") lateFlashes++; };
    await Task.Delay(650);
    Check(lateFlashes == 0 && !simon.IsPlaying, "Stopping Simon invalidates pending flashes");

    // Measure steady-state rendering; do not impose timing assertions on shared CI hosts.
    foreach (var t in tracks) { t.Samples = new short[48000 * 128]; Array.Fill(t.Samples, (short)1000); }
    mixer.SetActive(true); fx = PunchEffect.HalfSpeed; mixer.BeginEffect(fx);
    var block = new byte[480 * 4];
    for (var i = 0; i < 100; i++) mixer.Read(block, 0, block.Length);
    var before = GC.GetAllocatedBytesForCurrentThread();
    var timer = Stopwatch.StartNew();
    for (var i = 0; i < 2000; i++) mixer.Read(block, 0, block.Length);
    timer.Stop(); var allocated = GC.GetAllocatedBytesForCurrentThread() - before - 40; // Stopwatch instance.
    Check(allocated <= 64, "Steady-state four-track mixer allocates no per-buffer objects");
    Console.WriteLine($"BENCHMARK four 128-second tracks + half-speed FX: {timer.Elapsed.TotalMilliseconds / 2000:0.000} ms per 10 ms buffer; {allocated} bytes across 2,000 buffers.");
    Console.WriteLine($"All {checks} regression checks passed.");
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}
finally
{
    // root is a fresh task-owned directory, never a user's session directory.
    if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("SCSCompanion-tests-"))
        Directory.Delete(root, recursive: true);
}
