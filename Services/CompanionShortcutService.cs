using System.Diagnostics;
using System.Runtime.InteropServices;
using SCSCompanion.Models;

namespace SCSCompanion.Services;

public sealed class CompanionShortcutService
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 2;
    private const ushort Ctrl = 0x11, Shift = 0x10, Alt = 0x12;
    private static readonly Dictionary<int, (ushort Key, ushort[] Mods, string Name)> Audacity = new()
    {
        [0x6D] = (0x52, [Shift], "Record new track"), [0x6E] = (0x20, [], "Play / stop"),
        [0x6F] = (0x50, [], "Pause"), [0x70] = (0x4B, [], "Stop"),
        [0x2C] = (0x5A, [Ctrl], "Undo"), [0x2E] = (0x5A, [Ctrl, Shift], "Redo"),
        [0x30] = (0x31, [Ctrl], "Zoom in"), [0x32] = (0x33, [Ctrl], "Zoom out"),
    };
    private static readonly Dictionary<int, (ushort Key, ushort[] Mods, string Name)> Discord = new()
    {
        [0x6D] = (0x4D, [Ctrl, Shift], "Toggle mute"), [0x6E] = (0x44, [Ctrl, Shift], "Toggle deafen"),
        [0x6F] = (0x55, [Ctrl, Shift], "Toggle voice mode"), [0x70] = (0x2F, [Ctrl], "Discord shortcuts"),
        [0x2C] = (0x5D, [Ctrl], "Next channel"), [0x2E] = (0x5B, [Ctrl], "Previous channel"),
        [0x30] = (0x46, [Ctrl], "Search"), [0x32] = (0x1B, [], "Dismiss"),
    };

    private bool gainTouched;
    private int? lastGain;
    public event EventHandler<string>? ActionReported;

    public void Handle(MidiActivity activity, bool enabled, string module)
    {
        if (!enabled) { gainTouched = false; lastGain = null; return; }
        if (activity.Kind is "Note on" or "Note off" && activity.Data1 == 0x07)
        {
            gainTouched = activity.Kind == "Note on"; lastGain = null; return;
        }
        if (activity.Kind == "Control" && activity.Data1 == 0x07 && gainTouched)
        {
            if (lastGain is int previous && Math.Abs(activity.Data2 - previous) is > 0 and < 24)
            {
                SendMediaVolume(activity.Data2 > previous);
                ActionReported?.Invoke(this, activity.Data2 > previous ? "Volume up" : "Volume down");
            }
            lastGain = activity.Data2; return;
        }
        if (activity.Kind != "Note on") return;
        var map = module == "Audacity" ? Audacity : Discord;
        if (!map.TryGetValue(activity.Data1, out var action)) return;
        if (SendChord(action.Key, action.Mods)) ActionReported?.Invoke(this, action.Name);
    }

    public void ReleaseAll() { gainTouched = false; lastGain = null; }

    private static void SendMediaVolume(bool up) => SendChord(up ? (ushort)0xAF : (ushort)0xAE, []);
    private static bool SendChord(ushort key, ushort[] modifiers)
    {
        var list = new List<Input>();
        list.AddRange(modifiers.Select(k => Key(k, false))); list.Add(Key(key, false)); list.Add(Key(key, true));
        for (var i = modifiers.Length - 1; i >= 0; i--) list.Add(Key(modifiers[i], true));
        var data = list.ToArray();
        return SendInput((uint)data.Length, data, Marshal.SizeOf<Input>()) == data.Length;
    }
    private static Input Key(ushort key, bool up) => new() { Type = InputKeyboard, Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = up ? KeyUp : 0 } } };
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey, ScanCode; public uint Flags, Time; public nint ExtraInfo; }
}
