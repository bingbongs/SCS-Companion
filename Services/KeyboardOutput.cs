using System.Runtime.InteropServices;

namespace SCSCompanion.Services;

internal static class KeyboardOutput
{
    // INPUT's union includes MOUSEINPUT even when sending only keyboard events.
    internal static int InputSize => Marshal.SizeOf<Input>();

    public static bool SendChord(ushort key, params ushort[] modifiers)
    {
        var inputs = new List<Input>();
        var pressed = modifiers.Distinct().Where(k => (GetAsyncKeyState(k) & 0x8000) == 0).ToArray();
        foreach (var modifier in pressed) inputs.Add(Key(modifier, false));
        inputs.Add(Key(key, false));
        inputs.Add(Key(key, true));
        for (var i = pressed.Length - 1; i >= 0; i--) inputs.Add(Key(pressed[i], true));
        var data = inputs.ToArray();
        var sent = SendInput((uint)data.Length, data, InputSize);
        if (sent == data.Length) return true;
        // A partial send must not leave our modifiers held down.
        if (sent > 0)
        {
            var releases = new[] { key }.Concat(pressed.Reverse()).Select(k => Key(k, true)).ToArray();
            SendInput((uint)releases.Length, releases, InputSize);
        }
        return false;
    }

    private static Input Key(ushort key, bool up) => new()
    {
        Type = 1,
        Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = up ? 2u : 0u } },
    };

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public nint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nint ExtraInfo;
    }
}
