using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SCSCompanion.Models;

namespace SCSCompanion.Services;

public sealed class KaossPerformanceService : IDisposable
{
    private readonly KaossWaveProvider provider = new();
    private WasapiOut? output;
    private bool active;
    private bool surfaceHeld;
    private bool gateArp;
    private DateTimeOffset lastTap;
    private int program;
    private int loadGeneration;
    private bool disposed;
    public string ProgramName => provider.SampleLoaded && program == 5 ? "SAMPLE" : new[] { "SINE", "BASS", "LEAD", "PLUCK", "NOISE", "SAMPLE" }[program];
    public string SampleName { get; private set; } = "DROP AUDIO HERE";
    public double Bpm { get; private set; } = 120;
    public bool Hold => provider.Hold;
    public bool GateArp => gateArp;
    public event EventHandler<string>? StateChanged;

    public KaossPerformanceService(string? outputDeviceId) => ConfigureOutput(outputDeviceId);

    public void ConfigureOutput(string? deviceId)
    {
        var wasActive = active; output?.Stop(); output?.Dispose(); output = null;
        try
        {
            MMDevice? device = null;
            using var enumerator = new MMDeviceEnumerator();
            if (!string.IsNullOrWhiteSpace(deviceId)) device = enumerator.GetDevice(deviceId);
            output = device is null ? new WasapiOut(AudioClientShareMode.Shared, true, 45) : new WasapiOut(device, AudioClientShareMode.Shared, true, 45);
            output.Init(provider); device?.Dispose(); if (wasActive) output.Play();
        }
        catch (Exception ex) { output?.Dispose(); output = null; StateChanged?.Invoke(this, $"Audio unavailable · {ex.Message}"); }
    }

    public void SetActive(bool value)
    {
        active = value; provider.Active = value;
        if (value) output?.Play(); else { provider.Gate = false; provider.Hold = false; surfaceHeld = false; output?.Pause(); }
    }

    public async Task LoadSampleAsync(string path)
    {
        var generation = Interlocked.Increment(ref loadGeneration);
        try
        {
            var samples = await Task.Run(() => DecodeMono(path));
            if (disposed || generation != loadGeneration) return;
            provider.SetSample(samples); SampleName = Path.GetFileName(path).ToUpperInvariant(); program = 5; provider.Program = program;
            StateChanged?.Invoke(this, $"Sample loaded · {Path.GetFileName(path)}");
        }
        catch (Exception ex) { StateChanged?.Invoke(this, $"Could not load sample · {ex.Message}"); }
    }

    public void Handle(MidiActivity a, bool enabled)
    {
        if (!enabled) { provider.Gate = false; surfaceHeld = false; return; }
        if (a.Kind is "Note on" or "Note off")
        {
            var down = a.Kind == "Note on";
            if (a.Data1 == 0x01) { surfaceHeld = down; provider.Gate = down || provider.Hold; StateChanged?.Invoke(this, down ? "Touch performance" : ProgramName); return; }
            if (!down) return;
            switch (a.Data1)
            {
                case 0x2C: provider.Hold = !provider.Hold; provider.Gate = provider.Hold || surfaceHeld; break;
                case 0x2E: gateArp = !gateArp; provider.GateArp = gateArp; break;
                case 0x30: TapTempo(a.Timestamp); break;
                case 0x32: program = (program + 1) % 6; provider.Program = program; break;
                case >= 0x6D and <= 0x70: provider.Phrase = a.Data1 - 0x6D; break;
            }
            StateChanged?.Invoke(this, $"{ProgramName} · {Bpm:0} BPM"); return;
        }
        if (a.Kind != "Control") return;
        switch (a.Data1)
        {
            case 0x02: provider.X = a.Data2 / 127f; break;
            case 0x01: provider.Y = a.Data2 / 127f; break;
            case 0x62: program = Math.Clamp(a.Data2 / 22, 0, 5); provider.Program = program; break;
            case 0x07: provider.Volume = a.Data2 / 127f * 0.8f; break;
            case 0x03: Bpm = 60 + a.Data2 / 127d * 140; provider.Bpm = Bpm; break;
        }
    }

    private void TapTempo(DateTimeOffset now)
    {
        var delta = (now - lastTap).TotalSeconds;
        if (delta is > .25 and < 2) Bpm = Math.Clamp(60 / delta, 40, 240);
        lastTap = now; provider.Bpm = Bpm;
    }
    internal static float[] DecodeMono(string path)
    {
        using WaveStream reader = Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".flac", StringComparison.OrdinalIgnoreCase)
            ? new MediaFoundationReader(path) : new AudioFileReader(path);
        ISampleProvider source = reader.ToSampleProvider();
        if (source.WaveFormat.Channels == 2) source = new StereoToMonoSampleProvider(source);
        else if (source.WaveFormat.Channels != 1) throw new InvalidDataException("Use a mono or stereo sample.");
        if (source.WaveFormat.SampleRate != 48000) source = new WdlResamplingSampleProvider(source, 48000);
        const int maximumSamples = 48000 * 60 * 10;
        var data = new List<float>();
        var buffer = new float[4096];
        while (data.Count < maximumSamples)
        {
            var read = source.Read(buffer, 0, Math.Min(buffer.Length, maximumSamples - data.Count));
            if (read == 0) break;
            for (var i = 0; i < read; i++) data.Add(float.IsFinite(buffer[i]) ? Math.Clamp(buffer[i], -1, 1) : 0);
        }
        if (data.Count == 0) throw new InvalidDataException("The audio file is empty.");
        return data.ToArray();
    }
    public void Dispose() { disposed = true; Interlocked.Increment(ref loadGeneration); SetActive(false); output?.Dispose(); }
}

internal sealed class KaossWaveProvider : WaveProvider32
{
    private readonly object sync = new();
    private static readonly int[] Scale = [0, 2, 3, 5, 7, 10, 12, 14];
    private double phase, filter, arpPhase, samplePosition;
    private float[]? sample;
    public bool Active, Gate, Hold, GateArp;
    public int Program, Phrase;
    public float X = .5f, Y = .5f, Volume = .5f;
    public double Bpm = 120;
    public bool SampleLoaded => sample is { Length: > 0 };
    public KaossWaveProvider() => SetWaveFormat(48000, 2);
    public void SetSample(float[] value) { lock (sync) { sample = value; samplePosition = 0; } }
    public override int Read(float[] buffer, int offset, int count)
    {
        lock (sync)
        {
            var frames = count / 2; var rate = WaveFormat.SampleRate;
            var degree = Math.Clamp((int)(X * Scale.Length), 0, Scale.Length - 1);
            var frequency = 110 * Math.Pow(2, (Scale[degree] + Phrase * 12) / 12d);
            for (var n = 0; n < frames; n++)
            {
                phase = (phase + frequency / rate) % 1; arpPhase = (arpPhase + Bpm / 60d * 4 / rate) % 1;
                var gate = Active && Gate && (!GateArp || arpPhase < (.12 + Y * .75));
                double raw = 0;
                if (gate)
                {
                    raw = Program switch
                    {
                        0 => Math.Sin(phase * Math.Tau),
                        1 => Math.Tanh((2 * phase - 1) * (2 + Y * 6)),
                        2 => (2 * phase - 1) * .75 + Math.Sin(phase * Math.Tau) * .25,
                        3 => Math.Sin(phase * Math.Tau) * Math.Pow(1 - arpPhase, 3),
                        4 => Random.Shared.NextDouble() * 2 - 1,
                        5 => ReadSample(frequency / 220d),
                        _ => 0,
                    };
                }
                var alpha = .015 + Y * .35; filter += (raw - filter) * alpha;
                var value = (float)(filter * Volume * .42); buffer[offset + n * 2] = value; buffer[offset + n * 2 + 1] = value;
            }
            if (count % 2 != 0) buffer[offset + count - 1] = 0;
            return count;
        }
    }
    private double ReadSample(double speed)
    {
        if (sample is not { Length: > 1 }) return 0;
        var index = (int)samplePosition;
        var next = index + 1 == sample.Length ? 0 : index + 1;
        var value = sample[index] + (sample[next] - sample[index]) * (samplePosition - index);
        samplePosition = (samplePosition + speed) % sample.Length;
        return value;
    }
}
