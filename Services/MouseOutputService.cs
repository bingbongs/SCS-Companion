using System.Runtime.InteropServices;

namespace SCSCompanion.Services;

public sealed class MouseOutputService
{
    private const uint InputMouse = 0;
    private const uint MoveFlag = 0x0001;
    private const uint LeftDownFlag = 0x0002;
    private const uint LeftUpFlag = 0x0004;
    private const uint RightDownFlag = 0x0008;
    private const uint RightUpFlag = 0x0010;
    private const uint MiddleDownFlag = 0x0020;
    private const uint MiddleUpFlag = 0x0040;
    private const uint XDownFlag = 0x0080;
    private const uint XUpFlag = 0x0100;
    private const uint WheelFlag = 0x0800;
    private const uint XButton1 = 0x0001;

    private bool leftHeld;
    private bool rightHeld;
    private bool middleHeld;
    private bool x1Held;

    public bool Move(int x, int y) => x == 0 && y == 0 || Send(x, y, 0, MoveFlag);

    public bool Scroll(int delta) => delta == 0 || Send(0, 0, unchecked((uint)delta), WheelFlag);

    public bool SetLeft(bool down)
    {
        if (leftHeld == down)
        {
            return true;
        }

        leftHeld = down;
        return Send(0, 0, 0, down ? LeftDownFlag : LeftUpFlag);
    }

    public bool SetRight(bool down)
    {
        if (rightHeld == down)
        {
            return true;
        }

        rightHeld = down;
        return Send(0, 0, 0, down ? RightDownFlag : RightUpFlag);
    }

    public bool SetMiddle(bool down)
    {
        if (middleHeld == down)
        {
            return true;
        }

        middleHeld = down;
        return Send(0, 0, 0, down ? MiddleDownFlag : MiddleUpFlag);
    }

    public bool SetBack(bool down)
    {
        if (x1Held == down)
        {
            return true;
        }

        x1Held = down;
        return Send(0, 0, XButton1, down ? XDownFlag : XUpFlag);
    }

    public bool ClickLeft() => SetLeft(true) && SetLeft(false);

    public void ReleaseAll()
    {
        _ = SetLeft(false);
        _ = SetRight(false);
        _ = SetMiddle(false);
        _ = SetBack(false);
    }

    private static bool Send(int x, int y, uint data, uint flags)
    {
        var input = new Input
        {
            Type = InputMouse,
            Union = new InputUnion
            {
                Mouse = new MouseInput
                {
                    X = x,
                    Y = y,
                    MouseData = data,
                    Flags = flags,
                },
            },
        };

        return SendInput(1, [input], Marshal.SizeOf<Input>()) == 1;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }
}
