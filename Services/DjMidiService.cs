using Microsoft.UI.Dispatching;
using SCSCompanion.Models;
using Windows.Devices.Enumeration;
using Windows.Devices.Midi;

namespace SCSCompanion.Services;

public sealed class DjMidiService : IDisposable
{
    private const byte OutputChannel = 15;
    private static readonly Dictionary<int, byte> ButtonNotes = new()
    {
        [0x6D] = 0x10, // PLAY
        [0x6E] = 0x11, // CUE
        [0x6F] = 0x12, // SYNC
        [0x70] = 0x13, // TAP
        [0x2C] = 0x20, // Key Lock
        [0x2E] = 0x21, // Quantize
        [0x30] = 0x22, // Slip
        [0x32] = 0x23, // Censor / Reverse
    };
    private static readonly Dictionary<int, byte> ContinuousControls = new()
    {
        [0x07] = 0x28, // GAIN strip
        [0x03] = 0x29, // PITCH strip
        [0x01] = 0x2A, // Center strip
    };

    private readonly DispatcherQueue dispatcherQueue;
    private readonly HashSet<byte> heldNotes = [];
    private DeviceWatcher? watcher;
    private IMidiOutPort? outputPort;
    private string? connectedDeviceId;
    private bool disposed;
    private bool isRefreshing;
    private bool refreshAgain;
    private DateTimeOffset lastRingPulse;

    public DjMidiService(DispatcherQueue dispatcherQueue)
    {
        this.dispatcherQueue = dispatcherQueue;
    }

    public event EventHandler<string>? StatusChanged;

    public bool IsReady => outputPort is not null;

    public string EndpointName { get; private set; } = "Not connected";

    public static bool SupportsSubmode(string submode) =>
        submode is "Serato" or "Mixxx" or "VirtualDJ";

    public void Start()
    {
        if (watcher is not null)
        {
            return;
        }

        watcher = DeviceInformation.CreateWatcher(MidiOutPort.GetDeviceSelector());
        watcher.Added += OnDevicesChanged;
        watcher.Removed += OnDevicesChanged;
        watcher.Updated += OnDevicesChanged;
        watcher.EnumerationCompleted += OnEnumerationCompleted;
        watcher.Start();
        PublishStatus("Looking for virtual MIDI…");
    }

    public void Handle(MidiActivity activity, bool enabled, string submode)
    {
        if (!enabled || !SupportsSubmode(submode) || outputPort is null)
        {
            ReleaseAll();
            return;
        }

        if (activity.Kind is "Note on" or "Note off" && ButtonNotes.TryGetValue(activity.Data1, out var translatedNote))
        {
            SetNote(translatedNote, activity.Kind == "Note on");
            return;
        }

        if (activity.Kind == "Control" && ContinuousControls.TryGetValue(activity.Data1, out var translatedControl))
        {
            Send(new MidiControlChangeMessage(OutputChannel, translatedControl, (byte)activity.Data2));
            return;
        }

        if (activity.Kind == "Control" && activity.Data1 == 0x63)
        {
            var delta = activity.Data2 - 64;
            if (Math.Abs(delta) > 1 && activity.Timestamp - lastRingPulse >= TimeSpan.FromMilliseconds(45))
            {
                Pulse(delta > 0 ? (byte)0x30 : (byte)0x31);
                lastRingPulse = activity.Timestamp;
            }
        }
    }

    public void ReleaseAll()
    {
        foreach (var note in heldNotes.ToArray())
        {
            SetNote(note, false);
        }
    }

    private void OnDevicesChanged(DeviceWatcher sender, object args) => QueueRefresh();

    private void OnEnumerationCompleted(DeviceWatcher sender, object args) => QueueRefresh();

    private void QueueRefresh() => _ = dispatcherQueue.TryEnqueue(async () => await RefreshConnectionAsync());

    private async Task RefreshConnectionAsync()
    {
        if (disposed) return;
        if (isRefreshing)
        {
            refreshAgain = true;
            return;
        }

        isRefreshing = true;
        try
        {
            do
            {
                refreshAgain = false;
                var devices = await DeviceInformation.FindAllAsync(MidiOutPort.GetDeviceSelector());
                if (disposed) return;
                var device = devices.FirstOrDefault(candidate =>
                    candidate.Name.Contains("SCS Companion MIDI", StringComparison.OrdinalIgnoreCase))
                    ?? devices.FirstOrDefault(candidate =>
                    candidate.Name.Contains("To SCS.3 DaRouter", StringComparison.OrdinalIgnoreCase))
                    ?? devices.FirstOrDefault(candidate =>
                        candidate.Name.Contains("Bome", StringComparison.OrdinalIgnoreCase) ||
                        candidate.Name.Contains("loopMIDI", StringComparison.OrdinalIgnoreCase));

                if (device is null)
                {
                    Disconnect();
                    PublishStatus("No virtual MIDI output");
                    continue;
                }

                if (connectedDeviceId == device.Id && outputPort is not null)
                {
                    PublishReadyStatus();
                    continue;
                }

                Disconnect();
                try
                {
                    outputPort = await MidiOutPort.FromIdAsync(device.Id);
                    if (disposed) { Disconnect(); return; }
                    if (outputPort is null)
                    {
                        PublishStatus("Virtual MIDI port unavailable");
                        continue;
                    }

                    connectedDeviceId = device.Id;
                    EndpointName = device.Name;
                    PublishReadyStatus();
                }
                catch (Exception exception)
                {
                    PublishStatus($"Virtual MIDI unavailable · {exception.Message}");
                }
            }
            while (refreshAgain);
        }
        catch (Exception exception)
        {
            if (!disposed) PublishStatus($"MIDI refresh unavailable � {exception.Message}");
        }
        finally
        {
            isRefreshing = false;
        }
    }

    private void SetNote(byte note, bool down)
    {
        if (down)
        {
            if (heldNotes.Add(note))
            {
                Send(new MidiNoteOnMessage(OutputChannel, note, 0x7F));
            }
        }
        else if (heldNotes.Remove(note))
        {
            Send(new MidiNoteOffMessage(OutputChannel, note, 0));
        }
    }

    private void Pulse(byte note)
    {
        Send(new MidiNoteOnMessage(OutputChannel, note, 0x7F));
        Send(new MidiNoteOffMessage(OutputChannel, note, 0));
    }

    private void Send(IMidiMessage message)
    {
        try
        {
            outputPort?.SendMessage(message);
        }
        catch
        {
            // A watcher refresh will restore a removed or temporarily busy port.
        }
    }

    private void PublishStatus(string status) => StatusChanged?.Invoke(this, status);

    private void PublishReadyStatus()
    {
        var transport = EndpointName.Contains("SCS Companion MIDI", StringComparison.OrdinalIgnoreCase)
            ? "Built-in port"
            : "Compatibility port";
        PublishStatus($"{transport} · {EndpointName}");
    }

    private void Disconnect()
    {
        ReleaseAll();
        outputPort?.Dispose();
        outputPort = null;
        connectedDeviceId = null;
        EndpointName = "Not connected";
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (watcher is not null)
        {
            watcher.Added -= OnDevicesChanged;
            watcher.Removed -= OnDevicesChanged;
            watcher.Updated -= OnDevicesChanged;
            watcher.EnumerationCompleted -= OnEnumerationCompleted;
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                watcher.Stop();
            }
            watcher = null;
        }

        Disconnect();
    }
}
