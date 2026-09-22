using System.Runtime.InteropServices;
using SCSCompanion.Models;

namespace SCSCompanion.Services;

public sealed class ProductivityOutputService
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const ushort VkTab = 0x09;
    private const ushort VkReturn = 0x0D;
    private const ushort VkEscape = 0x1B;
    private const ushort VkLeft = 0x25;
    private const ushort VkUp = 0x26;
    private const ushort VkRight = 0x27;
    private const ushort VkDown = 0x28;
    private const ushort VkControl = 0x11;
    private const ushort VkShift = 0x10;
    private const ushort VkAlt = 0x12;
    private const ushort VkC = 0x43;
    private const ushort VkF = 0x46;
    private const ushort VkL = 0x4C;
    private const ushort VkR = 0x52;
    private const ushort VkV = 0x56;
    private const ushort VkY = 0x59;
    private const ushort VkZ = 0x5A;

    private readonly MediaOutputService mediaOutputService;
    private readonly MouseOutputService mouseOutput = new();
    private bool surfaceActive;
    private bool gainActive;
    private int? lastGainPosition;
    private int previousArrowSector = -1;
    private DateTimeOffset lastArrowSent;

    public ProductivityOutputService(MediaOutputService mediaOutputService)
    {
        this.mediaOutputService = mediaOutputService;
    }

    public event EventHandler<string>? ActionReported;

    public static bool SupportsSubmode(string submode) => submode is "Windows" or "Browser" or "Meetings" or "Streaming";

    public void Handle(MidiActivity activity, bool enabled, string submode)
    {
        if (!enabled || !SupportsSubmode(submode))
        {
            ReleaseAll();
            return;
        }

        if (activity.Kind is "Note on" or "Note off")
        {
            var down = activity.Kind == "Note on";
            if (activity.Data1 == 0x62)
            {
                surfaceActive = down;
                if (!down) previousArrowSector = -1;
                return;
            }
            if (activity.Data1 == 0x07)
            {
                gainActive = down && submode != "Meetings";
                lastGainPosition = null;
                return;
            }
            if (submode == "Meetings" && activity.Data1 is 0x2C or 0x2E or 0x30 or 0x32)
            {
                mediaOutputService.Handle(activity, enabled: true, submode: "Microphone");
                return;
            }
            if (down)
            {
                RouteButton(activity.Data1, submode);
            }
            return;
        }

        if (activity.Kind != "Control")
        {
            return;
        }

        if (submode == "Meetings" && activity.Data1 == 0x07)
        {
            mediaOutputService.Handle(activity, enabled: true, submode: "Microphone");
        }
        else if (activity.Data1 == 0x62 && surfaceActive)
        {
            RouteArrow(activity.Data2, activity.Timestamp);
        }
        else if (activity.Data1 == 0x07 && gainActive)
        {
            RouteScroll(activity.Data2);
        }
    }

    public void ReleaseAll()
    {
        surfaceActive = false;
        gainActive = false;
        lastGainPosition = null;
        previousArrowSector = -1;
        mouseOutput.ReleaseAll();
    }

    private void RouteButton(int note, string submode)
    {
        var result = submode switch
        {
            // Bind Ctrl+Alt+F13–F20 in OBS (Settings → Hotkeys).
            "Streaming" => RouteStreamingButton(note),
            "Browser" => RouteBrowserButton(note),
            "Meetings" => RouteMeetingButton(note),
            _ => RouteWindowsButton(note),
        };
        if (result.Sent)
        {
            ActionReported?.Invoke(this, result.Action);
        }
    }

    private static (bool Sent, string Action) RouteStreamingButton(int note)
    {
        var index = Array.IndexOf(CustomMappingService.Notes, note);
        return index < 0 ? (false, "") : (SendChord((ushort)(0x7C + index), VkControl, VkAlt), $"Streaming hotkey Ctrl+Alt+F{13 + index}");
    }

    private static (bool Sent, string Action) RouteWindowsButton(int note)
    {
        return note switch
        {
            0x6D => (SendChord(VkReturn), "Enter"),
            0x6E => (SendChord(VkEscape), "Escape"),
            0x6F => (SendChord(VkTab, VkAlt), "Switch application"),
            0x70 => (SendChord(VkTab), "Next field"),
            0x2C => (SendChord(VkZ, VkControl), "Undo"),
            0x2E => (SendChord(VkY, VkControl), "Redo"),
            0x30 => (SendChord(VkC, VkControl), "Copy"),
            0x32 => (SendChord(VkV, VkControl), "Paste"),
            _ => (false, string.Empty),
        };
    }

    private static (bool Sent, string Action) RouteBrowserButton(int note)
    {
        return note switch
        {
            0x6D => (SendChord(VkReturn), "Activate link"),
            0x6E => (SendChord(VkLeft, VkAlt), "Browser back"),
            0x6F => (SendChord(VkRight, VkAlt), "Browser forward"),
            0x70 => (SendChord(VkR, VkControl), "Refresh page"),
            0x2C => (SendChord(VkTab, VkControl, VkShift), "Previous tab"),
            0x2E => (SendChord(VkTab, VkControl), "Next tab"),
            0x30 => (SendChord(VkL, VkControl), "Focus address bar"),
            0x32 => (SendChord(VkF, VkControl), "Find on page"),
            _ => (false, string.Empty),
        };
    }

    private static (bool Sent, string Action) RouteMeetingButton(int note)
    {
        return note switch
        {
            0x6D => (SendChord(VkReturn), "Meeting activate"),
            0x6E => (SendChord(VkEscape), "Dismiss meeting panel"),
            0x6F => (SendChord(VkTab, VkAlt), "Switch meeting application"),
            0x70 => (SendChord(VkTab), "Next meeting control"),
            _ => (false, string.Empty),
        };
    }

    private void RouteArrow(int position, DateTimeOffset timestamp)
    {
        var sector = ((position + 16) / 32) % 4;
        if (sector == previousArrowSector && timestamp - lastArrowSent < TimeSpan.FromMilliseconds(170))
        {
            return;
        }

        var arrow = sector switch
        {
            0 => (VkUp, "Up"),
            1 => (VkRight, "Right"),
            2 => (VkDown, "Down"),
            _ => (VkLeft, "Left"),
        };
        if (SendChord(arrow.Item1))
        {
            previousArrowSector = sector;
            lastArrowSent = timestamp;
            ActionReported?.Invoke(this, $"Navigate {arrow.Item2}");
        }
    }

    private void RouteScroll(int position)
    {
        if (lastGainPosition is int previous)
        {
            var delta = position - previous;
            if (delta != 0 && Math.Abs(delta) <= 32 && mouseOutput.Scroll(delta * 18))
            {
                ActionReported?.Invoke(this, delta > 0 ? "Scroll up" : "Scroll down");
            }
        }
        lastGainPosition = position;
    }

    private static bool SendChord(ushort key, params ushort[] modifiers) => KeyboardOutput.SendChord(key, modifiers);
}
