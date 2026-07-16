using NAudio.CoreAudioApi;
using NAudio.Wave;
using SCSCompanion.Models;

namespace SCSCompanion.Services;

public sealed class PinkTromboneService : IDisposable
{
    private readonly VocalTractWaveProvider provider = new();
    private WasapiOut? output;
    public string VoiceName => provider.Voice switch { 0 => "CHEST", 1 => "BRIGHT", 2 => "WHISPER", _ => "ROBOT" };
    public event EventHandler<string>? StateChanged;
    public PinkTromboneService(string? outputDeviceId) => ConfigureOutput(outputDeviceId);
    public void ConfigureOutput(string? id)
    {
        var active = provider.Active; output?.Stop(); output?.Dispose();
        try
        {
            using var e = new MMDeviceEnumerator(); MMDevice? d = string.IsNullOrWhiteSpace(id) ? null : e.GetDevice(id);
            output = d is null ? new WasapiOut(AudioClientShareMode.Shared, true, 45) : new WasapiOut(d, AudioClientShareMode.Shared, true, 45);
            output.Init(provider); d?.Dispose(); if (active) output.Play();
        }
        catch (Exception ex) { StateChanged?.Invoke(this, $"Vocal audio unavailable · {ex.Message}"); }
    }
    public void SetActive(bool value) { provider.Active = value; provider.Gate = value && provider.Hold; if (value) output?.Play(); else output?.Pause(); }
    public void Handle(MidiActivity a, bool enabled)
    {
        if (!enabled) { provider.Gate = false; return; }
        if (a.Kind is "Note on" or "Note off")
        {
            var down = a.Kind == "Note on";
            if (a.Data1 == 0x01 || a.Data1 == 0x62) { provider.Gate = down || provider.Hold; return; }
            if (!down) return;
            if (a.Data1 == 0x2C) { provider.Hold = !provider.Hold; provider.Gate = provider.Hold; }
            else if (a.Data1 == 0x2E) provider.Nasal = !provider.Nasal;
            else if (a.Data1 is >= 0x6D and <= 0x70) provider.Voice = a.Data1 - 0x6D;
            StateChanged?.Invoke(this, $"{VoiceName} · {(provider.Nasal ? "NASAL" : "ORAL")}"); return;
        }
        if (a.Kind != "Control") return;
        switch (a.Data1)
        {
            case 0x02: provider.Tongue = a.Data2 / 127f; break;
            case 0x01: provider.Mouth = a.Data2 / 127f; break;
            case 0x62: provider.Pitch = 70 + a.Data2 / 127f * 250; break;
            case 0x03: provider.Pitch = 70 + a.Data2 / 127f * 250; break;
            case 0x07: provider.Volume = .1f + a.Data2 / 127f * .7f; break;
        }
    }
    public void Dispose() { provider.Active = false; output?.Dispose(); }
}

internal sealed class VocalTractWaveProvider : WaveProvider32
{
    private double phase, f1z1, f1z2, f2z1, f2z2, f3z1, f3z2;
    public bool Active, Gate, Hold, Nasal; public int Voice; public float Tongue=.5f, Mouth=.55f, Pitch=120, Volume=.5f;
    public VocalTractWaveProvider() => SetWaveFormat(48000, 2);
    public override int Read(float[] b, int o, int c)
    {
        var sr = WaveFormat.SampleRate;
        for (var i=0;i<c/2;i++)
        {
            phase=(phase+Pitch/sr)%1;
            var glottis = Voice switch { 2 => Random.Shared.NextDouble()*2-1, 3 => phase<.5?1:-1, _ => Math.Tanh((Math.Sin(phase*Math.Tau)+.35*Math.Sin(phase*Math.Tau*2))*3) };
            if (!Active || !Gate) glottis=0;
            var f1=250+Mouth*650; var f2=700+(1-Tongue)*1700; var f3=Nasal?2100:2700;
            var v = Resonator(glottis, f1, Voice==1?80:120, ref f1z1, ref f1z2)
                  + Resonator(glottis, f2, 170, ref f2z1, ref f2z2)*.55
                  + Resonator(glottis, f3, 240, ref f3z1, ref f3z2)*.25;
            var value=(float)Math.Clamp(v*Volume*.22,-.8,.8); b[o+i*2]=value; b[o+i*2+1]=value;
        }
        return c;
    }
    private static double Resonator(double x,double hz,double bandwidth,ref double z1,ref double z2)
    {
        const double sr=48000; var r=Math.Exp(-Math.PI*bandwidth/sr); var a=2*r*Math.Cos(2*Math.PI*hz/sr); var y=(1-r)*x+a*z1-r*r*z2; z2=z1; z1=y; return y;
    }
}
