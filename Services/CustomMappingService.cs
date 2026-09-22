using System.Text.Json;
using SCSCompanion.Models;

namespace SCSCompanion.Services;

public sealed class CustomMappingService
{
    public static readonly int[] Notes = [0x2C, 0x2E, 0x30, 0x32, 0x6D, 0x6E, 0x6F, 0x70];
    public static readonly string[] Controls = ["B11", "B12", "B13", "B14", "PLAY", "CUE", "SYNC", "TAP"];
    private readonly string path;
    private readonly Func<ushort, ushort[], bool> send;
    private readonly HashSet<int> held = [];
    private string[][] profiles = Enumerable.Range(0, 3).Select(_ => new string[8]).ToArray();
    public event EventHandler<string>? ActionReported;

    public CustomMappingService(string? storagePath = null, Func<ushort, ushort[], bool>? output = null)
    {
        path = storagePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCSCompanion", "custom-mappings.json");
        send = output ?? KeyboardOutput.SendChord;
        try
        {
            if (!File.Exists(path)) return;
            var loaded = JsonSerializer.Deserialize<string[][]>(File.ReadAllText(path));
            if (loaded is null || loaded.Length != 3 || loaded.Any(p => p is null || p.Length != 8)) return;
            if (loaded.SelectMany(p => p).Any(chord => !string.IsNullOrWhiteSpace(chord) && !TryParseChord(chord, out _, out _))) return;
            profiles = loaded;
        }
        catch (Exception) { /* An invalid profile must not prevent startup. */ }
    }

    public string GetChord(int profile, int control) => profiles[profile][control] ?? "";

    public bool SetChord(int profile, int control, string chord, out string message)
    {
        chord = chord.Trim();
        if (chord.Length > 0 && !TryParseChord(chord, out _, out _))
        {
            message = "Use one key with optional Ctrl, Alt, Shift or Win, for example Ctrl+Shift+K.";
            return false;
        }
        profiles[profile][control] = chord;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(path + ".tmp", path, true);
            message = chord.Length == 0 ? "Mapping cleared." : "Mapping saved.";
        }
        catch (Exception ex) { message = $"Mapping works for this session; could not save: {ex.Message}"; }
        return true;
    }

    public void ReleaseAll() => held.Clear();

    public void Handle(MidiActivity activity, bool enabled, int profile)
    {
        if (!enabled) { ReleaseAll(); return; }
        if (activity.Kind == "Note off") { held.Remove(activity.Data1); return; }
        if (activity.Kind != "Note on" || !held.Add(activity.Data1)) return;
        var control = Array.IndexOf(Notes, activity.Data1);
        if (control < 0) return;
        var chord = GetChord(profile, control);
        if (TryParseChord(chord, out var key, out var modifiers))
        {
            var sent = send(key, modifiers);
            ActionReported?.Invoke(this, sent ? chord : $"Blocked · {chord}");
        }
        else ActionReported?.Invoke(this, $"{Controls[control]} is unassigned · Settings → Custom");
    }

    internal static bool TryParseChord(string? chord, out ushort key, out ushort[] modifiers)
    {
        key = 0; modifiers = [];
        if (string.IsNullOrWhiteSpace(chord)) return false;
        var parts = chord.Split('+', StringSplitOptions.TrimEntries);
        var mods = new List<ushort>();
        foreach (var part in parts.Take(parts.Length - 1))
        {
            ushort modifier = part.ToUpperInvariant() switch { "CTRL" => 0x11, "ALT" => 0x12, "SHIFT" => 0x10, "WIN" => 0x5B, _ => 0 };
            if (modifier == 0 || mods.Contains(modifier)) return false;
            mods.Add(modifier);
        }
        var name = parts[^1].ToUpperInvariant();
        if (name.Length == 1 && (name[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')) key = name[0];
        else if (name.StartsWith('F') && int.TryParse(name.AsSpan(1), out var f) && f is >= 1 and <= 24) key = (ushort)(0x70 + f - 1);
        else key = name switch
        {
            "ENTER" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "BACKSPACE" => 0x08,
            "DELETE" => 0x2E,
            "INSERT" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "PLAYPAUSE" => 0xB3,
            "NEXTTRACK" => 0xB0,
            "PREVTRACK" => 0xB1,
            "MUTE" => 0xAD,
            "VOLUMEUP" => 0xAF,
            "VOLUMEDOWN" => 0xAE,
            _ => 0,
        };
        modifiers = mods.ToArray();
        return key != 0;
    }
}
