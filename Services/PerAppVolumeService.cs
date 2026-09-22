using NAudio.CoreAudioApi;
using SCSCompanion.Models;

namespace SCSCompanion.Services;

public sealed class PerAppVolumeService : IDisposable
{
    private MMDevice? device;
    private readonly List<AudioSessionControl> sessions = [];
    private int selected;
    private DateTime refreshed;
    public event EventHandler<string>? ActionReported;

    public void Handle(MidiActivity a)
    {
        if (a.Kind != "Note on" && !(a.Kind == "Control" && a.Data1 == 0x07)) return;
        try
        {
            if (DateTime.UtcNow - refreshed > TimeSpan.FromSeconds(2)) Refresh();
            if (sessions.Count == 0) { ActionReported?.Invoke(this, "No active audio apps · start playback first"); return; }
            if (a.Kind == "Note on" && a.Data1 is 0x2C or 0x2E)
                selected = (selected + (a.Data1 == 0x2C ? sessions.Count - 1 : 1)) % sessions.Count;
            var session = sessions[selected];
            var volume = session.SimpleAudioVolume;
            if (a.Kind == "Control") volume.Volume = a.Data2 / 127f;
            else if (a.Data1 is 0x6D or 0x70) volume.Mute = !volume.Mute;
            else if (a.Data1 == 0x30) volume.Volume = Math.Max(0, volume.Volume - .05f);
            else if (a.Data1 == 0x32) volume.Volume = Math.Min(1, volume.Volume + .05f);
            var name = session.DisplayName;
            if (string.IsNullOrWhiteSpace(name))
            {
                try { using var process = System.Diagnostics.Process.GetProcessById((int)session.GetProcessID); name = process.ProcessName; }
                catch { name = "System sounds"; }
            }
            ActionReported?.Invoke(this, $"{name} · {(volume.Mute ? "MUTED" : $"{volume.Volume * 100:0}%")}");
        }
        catch (Exception ex) { Dispose(); ActionReported?.Invoke(this, $"App mixer unavailable · {ex.Message}"); }
    }

    private void Refresh()
    {
        var previousId = sessions.Count > 0 ? sessions[selected].GetSessionInstanceIdentifier : null;
        Dispose();
        using var enumerator = new MMDeviceEnumerator();
        device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var collection = device.AudioSessionManager.Sessions;
        for (var i = 0; i < collection.Count; i++)
        {
            var session = collection[i];
            if (session.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired) sessions.Add(session);
            else session.Dispose();
        }
        selected = Math.Max(0, sessions.FindIndex(s => s.GetSessionInstanceIdentifier == previousId));
        refreshed = DateTime.UtcNow;
    }

    public void Dispose()
    {
        foreach (var session in sessions) session.Dispose();
        sessions.Clear(); device?.Dispose(); device = null;
        refreshed = DateTime.MinValue;
    }
}
