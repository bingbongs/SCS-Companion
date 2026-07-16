using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SCSCompanion.Models;

namespace SCSCompanion.Services;

public sealed class VrChatOscService : IDisposable
{
    private const string Vertical = "/input/Vertical";
    private const string Horizontal = "/input/Horizontal";
    private const string LookHorizontal = "/input/LookHorizontal";
    private static readonly string[] AllButtons =
    [
        "/input/Jump", "/input/Run", "/input/Voice", "/input/QuickMenuToggleLeft", "/input/QuickMenuToggleRight",
        "/input/LookLeft", "/input/LookRight", "/input/ComfortLeft", "/input/ComfortRight",
        "/input/UseLeft", "/input/UseRight", "/input/GrabLeft", "/input/GrabRight",
    ];

    private readonly UdpClient client = new();
    private readonly Dictionary<string, int> buttonStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> axisStates = new(StringComparer.Ordinal);
    private bool surfaceActive;
    private bool pitchActive;
    private bool centerActive;
    private bool turnLeftActive;
    private bool turnRightActive;
    private bool routingActive;
    private bool menuPointerActive;
    private double precisionScale = 0.5;

    public VrChatOscService()
    {
        client.Connect(IPAddress.Loopback, 9000);
        Status = "Ready · 127.0.0.1:9000";
    }

    public event EventHandler<string>? StatusChanged;

    public event EventHandler<bool>? MenuPointerChanged;

    public string Status { get; }

    public bool MenuPointerActive => menuPointerActive;

    public static bool SupportsSubmode(string submode) =>
        submode is "Auto" or "Desktop" or "PC VR";

    public void Start() => StatusChanged?.Invoke(this, Status);

    public void Handle(MidiActivity activity, bool enabled, string submode)
    {
        if (!enabled || !SupportsSubmode(submode))
        {
            if (routingActive)
            {
                ReleaseAll();
            }
            return;
        }

        routingActive = true;

        if (activity.Kind is "Note on" or "Note off")
        {
            var down = activity.Kind == "Note on";
            switch (activity.Data1)
            {
                case 0x62:
                    surfaceActive = down;
                    if (!down)
                    {
                        SetAxis(Horizontal, 0);
                        SetAxis(Vertical, 0);
                    }
                    return;
                case 0x01:
                    centerActive = down;
                    return;
                case 0x03:
                    pitchActive = down;
                    if (!down)
                    {
                        SetAxis(LookHorizontal, 0);
                    }
                    return;
                case 0x6D:
                    SetButton("/input/Jump", down);
                    return;
                case 0x6E:
                    SetButton("/input/Run", down);
                    return;
                case 0x6F:
                    SetButton("/input/Voice", down);
                    return;
                case 0x70:
                    SetButton("/input/QuickMenuToggleRight", down);
                    return;
                case 0x2C:
                    turnLeftActive = down;
                    UpdateButtonTurning();
                    return;
                case 0x2E:
                    turnRightActive = down;
                    UpdateButtonTurning();
                    return;
                case 0x30:
                    if (down)
                    {
                        SetMenuPointerActive(false);
                    }
                    SetButton("/input/QuickMenuToggleLeft", down);
                    return;
                case 0x32:
                    if (down)
                    {
                        SetMenuPointerActive(!menuPointerActive);
                        SetAxis(Horizontal, 0);
                        SetAxis(Vertical, 0);
                        surfaceActive = false;
                    }
                    SetButton("/input/QuickMenuToggleRight", down);
                    return;
            }
            return;
        }

        if (activity.Kind != "Control")
        {
            return;
        }

        if (activity.Data1 == 0x01 && centerActive)
        {
            precisionScale = 0.05 + (activity.Data2 / 127d * 0.95);
            UpdateButtonTurning();
            return;
        }

        if (activity.Data1 == 0x03 && pitchActive)
        {
            var look = Math.Clamp((activity.Data2 - 64) / 63f, -1f, 1f);
            SetAxis(LookHorizontal, (float)(look * precisionScale));
            return;
        }

        if (activity.Data1 == 0x62 && surfaceActive && !menuPointerActive)
        {
            var angle = (activity.Data2 / 128d * Math.Tau) - (Math.PI / 2d);
            var horizontal = (float)(Math.Cos(angle) * precisionScale);
            var vertical = (float)(-Math.Sin(angle) * precisionScale);
            SetAxis(Horizontal, horizontal);
            SetAxis(Vertical, vertical);
        }
    }

    public void ReleaseAll()
    {
        if (!routingActive && !surfaceActive && !pitchActive && !centerActive && !menuPointerActive &&
            buttonStates.Count == 0 && axisStates.Count == 0)
        {
            return;
        }

        routingActive = false;
        surfaceActive = false;
        pitchActive = false;
        centerActive = false;
        turnLeftActive = false;
        turnRightActive = false;
        SetMenuPointerActive(false);
        SendFloat(Vertical, 0);
        SendFloat(Horizontal, 0);
        SendFloat(LookHorizontal, 0);
        axisStates.Clear();
        foreach (var address in AllButtons)
        {
            SendInt(address, 0);
        }
        buttonStates.Clear();
    }

    private void UpdateButtonTurning()
    {
        var direction = (turnRightActive ? 1f : 0f) - (turnLeftActive ? 1f : 0f);
        if (direction != 0 || !pitchActive)
        {
            SetAxis(LookHorizontal, (float)(direction * precisionScale));
        }
    }

    private void SetMenuPointerActive(bool active)
    {
        if (menuPointerActive == active)
        {
            return;
        }

        menuPointerActive = active;
        MenuPointerChanged?.Invoke(this, active);
    }

    private void SetAxis(string address, float value)
    {
        value = Math.Clamp(value, -1f, 1f);
        if (axisStates.TryGetValue(address, out var previous) && Math.Abs(previous - value) < 0.01f)
        {
            return;
        }
        axisStates[address] = value;
        SendFloat(address, value);
    }

    private void SetButton(string address, bool down)
    {
        var value = down ? 1 : 0;
        if (buttonStates.TryGetValue(address, out var previous) && previous == value)
        {
            return;
        }
        buttonStates[address] = value;
        SendInt(address, value);
    }

    private void SendInt(string address, int value) => Send(BuildMessage(address, 'i', value));

    private void SendFloat(string address, float value) => Send(BuildMessage(address, 'f', BitConverter.SingleToInt32Bits(value)));

    private void Send(byte[] packet)
    {
        try
        {
            _ = client.Send(packet, packet.Length);
        }
        catch (SocketException exception)
        {
            StatusChanged?.Invoke(this, $"OSC error · {exception.SocketErrorCode}");
        }
        catch (ObjectDisposedException)
        {
            // Shutdown already owns the socket.
        }
    }

    private static byte[] BuildMessage(string address, char type, int valueBits)
    {
        var addressLength = PaddedLength(address.Length + 1);
        var typeLength = PaddedLength(3);
        var packet = new byte[addressLength + typeLength + 4];
        Encoding.ASCII.GetBytes(address, packet.AsSpan(0, address.Length));
        packet[addressLength] = (byte)',';
        packet[addressLength + 1] = (byte)type;
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(addressLength + typeLength, 4), valueBits);
        return packet;
    }

    private static int PaddedLength(int length) => (length + 3) & ~3;

    public void Dispose()
    {
        ReleaseAll();
        client.Dispose();
    }
}
