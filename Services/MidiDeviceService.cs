using Microsoft.UI.Dispatching;
using SCSCompanion.Models;
using Windows.Devices.Enumeration;
using Windows.Devices.Midi;
using Windows.Storage.Streams;

namespace SCSCompanion.Services;

public sealed class MidiDeviceService : IDisposable
{
    private readonly DispatcherQueue dispatcherQueue;
    private DeviceWatcher? inputWatcher;
    private DeviceWatcher? outputWatcher;
    private MidiInPort? inputPort;
    private IMidiOutPort? outputPort;
    private string? connectedInputDeviceId;
    private string? connectedOutputDeviceId;
    private bool disposed;
    private bool isRefreshing;
    private bool refreshAgain;

    public MidiDeviceService(DispatcherQueue dispatcherQueue)
    {
        this.dispatcherQueue = dispatcherQueue;
    }

    public event EventHandler<string>? StatusChanged;

    public event EventHandler<MidiActivity>? ActivityReceived;

    public event EventHandler? OutputReady;

    public bool IsOutputReady => outputPort is not null;

    public void Start()
    {
        if (inputWatcher is not null || outputWatcher is not null)
        {
            return;
        }

        inputWatcher = CreateWatcher(MidiInPort.GetDeviceSelector());
        outputWatcher = CreateWatcher(MidiOutPort.GetDeviceSelector());
        inputWatcher.Start();
        outputWatcher.Start();
        PublishStatus("Looking for SCS.3d…");
    }

    public bool SendNote(byte note, byte velocity)
    {
        try
        {
            outputPort?.SendMessage(new MidiNoteOnMessage(0, note, velocity));
            return outputPort is not null;
        }
        catch
        {
            return false;
        }
    }

    public bool SendControlChange(byte controller, byte value)
    {
        try
        {
            outputPort?.SendMessage(new MidiControlChangeMessage(0, controller, value));
            return outputPort is not null;
        }
        catch
        {
            return false;
        }
    }

    public bool ClearLeds()
    {
        try
        {
            outputPort?.SendMessage(new MidiControlChangeMessage(0, 0x7B, 0));
            return outputPort is not null;
        }
        catch
        {
            return false;
        }
    }

    private DeviceWatcher CreateWatcher(string selector)
    {
        var deviceWatcher = DeviceInformation.CreateWatcher(selector);
        deviceWatcher.Added += OnDevicesChanged;
        deviceWatcher.Removed += OnDevicesChanged;
        deviceWatcher.Updated += OnDevicesChanged;
        deviceWatcher.EnumerationCompleted += OnEnumerationCompleted;
        return deviceWatcher;
    }

    private void OnDevicesChanged(DeviceWatcher sender, object args) => QueueRefresh();

    private void OnEnumerationCompleted(DeviceWatcher sender, object args) => QueueRefresh();

    private void QueueRefresh()
    {
        _ = dispatcherQueue.TryEnqueue(async () => await RefreshConnectionAsync());
    }

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
                var inputDevices = await DeviceInformation.FindAllAsync(MidiInPort.GetDeviceSelector());
                var outputDevices = await DeviceInformation.FindAllAsync(MidiOutPort.GetDeviceSelector());
                if (disposed) return;
                var inputDevice = inputDevices.FirstOrDefault(candidate =>
                    candidate.Name.Contains("SCS.3d", StringComparison.OrdinalIgnoreCase));
                var outputDevice = outputDevices.FirstOrDefault(candidate =>
                    candidate.Name.Contains("SCS.3d", StringComparison.OrdinalIgnoreCase));

                if (inputDevice is null)
                {
                    DisconnectInput();
                    DisconnectOutput();
                    PublishStatus("SCS.3d disconnected");
                    continue;
                }

                if (connectedInputDeviceId != inputDevice.Id || inputPort is null)
                {
                    DisconnectInput();
                    try
                    {
                        inputPort = await MidiInPort.FromIdAsync(inputDevice.Id);
                        if (disposed) { DisconnectInput(); return; }
                        if (inputPort is null)
                        {
                            PublishStatus("SCS.3d found · MIDI input unavailable");
                            continue;
                        }

                        connectedInputDeviceId = inputDevice.Id;
                        inputPort.MessageReceived += OnMessageReceived;
                    }
                    catch (Exception exception)
                    {
                        PublishStatus($"SCS.3d found · {exception.Message}");
                        continue;
                    }
                }

                var openedOutput = false;
                if (outputDevice is null)
                {
                    DisconnectOutput();
                }
                else if (connectedOutputDeviceId != outputDevice.Id || outputPort is null)
                {
                    DisconnectOutput();
                    try
                    {
                        outputPort = await MidiOutPort.FromIdAsync(outputDevice.Id);
                        if (disposed) { DisconnectOutput(); return; }
                        if (outputPort is not null)
                        {
                            connectedOutputDeviceId = outputDevice.Id;
                            openedOutput = true;
                        }
                    }
                    catch
                    {
                        DisconnectOutput();
                    }
                }

                PublishStatus(outputPort is not null
                    ? $"Connected · {inputDevice.Name} · LEDs ready"
                    : $"Connected · {inputDevice.Name} · input only");
                if (openedOutput)
                {
                    OutputReady?.Invoke(this, EventArgs.Empty);
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

    private void OnMessageReceived(MidiInPort sender, MidiMessageReceivedEventArgs args)
    {
        using var reader = DataReader.FromBuffer(args.Message.RawData);
        var bytes = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(bytes);
        var activity = MidiActivity.FromRaw(bytes);
        _ = dispatcherQueue.TryEnqueue(() => { if (!disposed) ActivityReceived?.Invoke(this, activity); });
    }

    private void PublishStatus(string status)
    {
        StatusChanged?.Invoke(this, status);
    }

    private void DisconnectInput()
    {
        if (inputPort is not null)
        {
            inputPort.MessageReceived -= OnMessageReceived;
            inputPort.Dispose();
            inputPort = null;
        }

        connectedInputDeviceId = null;
    }

    private void DisconnectOutput()
    {
        if (outputPort is not null)
        {
            _ = ClearLeds();
            outputPort.Dispose();
            outputPort = null;
        }

        connectedOutputDeviceId = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        StopWatcher(ref inputWatcher);
        StopWatcher(ref outputWatcher);

        DisconnectInput();
        DisconnectOutput();
    }

    private void StopWatcher(ref DeviceWatcher? deviceWatcher)
    {
        if (deviceWatcher is null)
        {
            return;
        }

        deviceWatcher.Added -= OnDevicesChanged;
        deviceWatcher.Removed -= OnDevicesChanged;
        deviceWatcher.Updated -= OnDevicesChanged;
        deviceWatcher.EnumerationCompleted -= OnEnumerationCompleted;
        if (deviceWatcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            deviceWatcher.Stop();
        }

        deviceWatcher = null;
    }
}
