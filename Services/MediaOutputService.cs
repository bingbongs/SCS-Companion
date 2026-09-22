using System.Runtime.InteropServices;
using SCSCompanion.Models;

namespace SCSCompanion.Services;

public sealed class MediaOutputService : IDisposable
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const ushort VkMediaNextTrack = 0xB0;
    private const ushort VkMediaPreviousTrack = 0xB1;
    private const ushort VkMediaStop = 0xB2;
    private const ushort VkMediaPlayPause = 0xB3;
    private const ushort VkLaunchMediaSelect = 0xB5;
    private const ushort VkVolumeMute = 0xAD;
    private const ushort VkVolumeDown = 0xAE;
    private const ushort VkVolumeUp = 0xAF;

    private readonly PerAppVolumeService perAppVolume = new();
    private readonly EndpointVolumeController outputVolumeController = new(dataFlow: 0);
    private readonly EndpointVolumeController microphoneVolumeController = new(dataFlow: 1);

    public event EventHandler<string>? ActionReported;
    public event EventHandler<bool>? MicrophoneMuteChanged;

    public MediaOutputService() => perAppVolume.ActionReported += (_, action) => ActionReported?.Invoke(this, action);

    public bool IsMicrophoneMuted { get; private set; }

    public static bool SupportsSubmode(string submode) => submode is "System" or "Playback" or "Microphone" or "Per-app mixer";

    public void Handle(MidiActivity activity, bool enabled, string submode)
    {
        if (!enabled || !SupportsSubmode(submode))
        {
            return;
        }

        if (submode == "Per-app mixer") { perAppVolume.Handle(activity); return; }
        if (submode == "Microphone")
        {
            HandleMicrophone(activity);
            return;
        }

        if (activity.Kind == "Control" && activity.Data1 == 0x07)
        {
            var level = Math.Clamp(activity.Data2 / 127f, 0f, 1f);
            if (outputVolumeController.SetVolume(level))
            {
                ActionReported?.Invoke(this, $"System volume {level * 100:0}%");
            }
            return;
        }

        if (activity.Kind != "Note on")
        {
            return;
        }

        var action = activity.Data1 switch
        {
            0x6D => (VkMediaPlayPause, "Play / pause"),
            0x6E => (VkMediaPreviousTrack, "Previous track"),
            0x6F => (VkMediaNextTrack, "Next track"),
            0x70 => (VkVolumeMute, "Mute / unmute"),
            0x2C => (VkVolumeDown, "Volume down"),
            0x2E => (VkVolumeUp, "Volume up"),
            0x30 => (VkMediaStop, "Stop playback"),
            0x32 => (VkLaunchMediaSelect, "Open media app"),
            _ => ((ushort)0, string.Empty),
        };

        if (action.Item1 != 0 && SendKey(action.Item1))
        {
            ActionReported?.Invoke(this, action.Item2);
        }
    }

    public void RefreshMicrophoneState()
    {
        if (microphoneVolumeController.TryGetMute(out var muted))
        {
            PublishMicrophoneMute(muted);
        }
    }

    private void HandleMicrophone(MidiActivity activity)
    {
        if (activity.Kind == "Control" && activity.Data1 == 0x07)
        {
            var level = Math.Clamp(activity.Data2 / 127f, 0f, 1f);
            if (microphoneVolumeController.SetVolume(level))
            {
                ActionReported?.Invoke(this, $"Microphone level {level * 100:0}%");
            }
            return;
        }

        if (activity.Kind != "Note on")
        {
            return;
        }

        switch (activity.Data1)
        {
            case 0x2C:
                if (microphoneVolumeController.AdjustVolume(-0.05f, out var lowerLevel))
                {
                    ActionReported?.Invoke(this, $"Microphone level {lowerLevel * 100:0}%");
                }
                break;
            case 0x2E:
                if (microphoneVolumeController.AdjustVolume(0.05f, out var higherLevel))
                {
                    ActionReported?.Invoke(this, $"Microphone level {higherLevel * 100:0}%");
                }
                break;
            case 0x30:
            case 0x70:
                if (microphoneVolumeController.TryGetMute(out var muted) && microphoneVolumeController.SetMute(!muted))
                {
                    PublishMicrophoneMute(!muted);
                }
                break;
            case 0x32:
                if (microphoneVolumeController.SetMute(false))
                {
                    PublishMicrophoneMute(false);
                }
                break;
        }
    }

    private void PublishMicrophoneMute(bool muted)
    {
        IsMicrophoneMuted = muted;
        ActionReported?.Invoke(this, muted ? "Microphone muted" : "Microphone live");
        MicrophoneMuteChanged?.Invoke(this, muted);
    }

    private static bool SendKey(ushort virtualKey) => KeyboardOutput.SendChord(virtualKey);

    public void Dispose()
    {
        perAppVolume.Dispose();
        outputVolumeController.Dispose();
        microphoneVolumeController.Dispose();
    }

    private sealed class EndpointVolumeController(int dataFlow) : IDisposable
    {
        private static readonly Guid AudioEndpointVolumeGuid = typeof(IAudioEndpointVolume).GUID;
        private readonly object syncRoot = new();
        private object? endpointObject;
        private DateTime lastEndpointRefresh;

        public bool SetVolume(float level)
        {
            lock (syncRoot)
            {
                if ((endpointObject is null || DateTime.UtcNow - lastEndpointRefresh > TimeSpan.FromSeconds(2)) && !RefreshEndpoint())
                {
                    return false;
                }

                try
                {
                    return endpointObject is IAudioEndpointVolume endpointVolume &&
                           endpointVolume.SetMasterVolumeLevelScalar(level, Guid.Empty) == 0;
                }
                catch (COMException)
                {
                    ReleaseEndpoint();
                    return false;
                }
            }
        }

        public bool AdjustVolume(float delta, out float level)
        {
            level = 0f;
            lock (syncRoot)
            {
                if (!EnsureEndpoint() || endpointObject is not IAudioEndpointVolume endpointVolume ||
                    endpointVolume.GetMasterVolumeLevelScalar(out var current) != 0)
                {
                    return false;
                }

                level = Math.Clamp(current + delta, 0f, 1f);
                return endpointVolume.SetMasterVolumeLevelScalar(level, Guid.Empty) == 0;
            }
        }

        public bool TryGetMute(out bool muted)
        {
            muted = false;
            lock (syncRoot)
            {
                return EnsureEndpoint() && endpointObject is IAudioEndpointVolume endpointVolume &&
                       endpointVolume.GetMute(out muted) == 0;
            }
        }

        public bool SetMute(bool muted)
        {
            lock (syncRoot)
            {
                return EnsureEndpoint() && endpointObject is IAudioEndpointVolume endpointVolume &&
                       endpointVolume.SetMute(muted, Guid.Empty) == 0;
            }
        }

        private bool EnsureEndpoint() =>
            endpointObject is not null && DateTime.UtcNow - lastEndpointRefresh <= TimeSpan.FromSeconds(2) || RefreshEndpoint();

        private bool RefreshEndpoint()
        {
            object? enumeratorObject = null;
            object? deviceObject = null;
            try
            {
                ReleaseEndpoint();
                var type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
                if (type is null)
                {
                    return false;
                }

                enumeratorObject = Activator.CreateInstance(type);
                if (enumeratorObject is not IMMDeviceEnumerator enumerator)
                {
                    return false;
                }

                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(dataFlow, 1, out var device));
                deviceObject = device;
                var endpointVolumeGuid = AudioEndpointVolumeGuid;
                Marshal.ThrowExceptionForHR(device.Activate(ref endpointVolumeGuid, 23, nint.Zero, out var activatedEndpoint));
                endpointObject = activatedEndpoint;
                lastEndpointRefresh = DateTime.UtcNow;
                return endpointObject is IAudioEndpointVolume;
            }
            catch (COMException)
            {
                ReleaseEndpoint();
                return false;
            }
            finally
            {
                if (deviceObject is not null && Marshal.IsComObject(deviceObject)) Marshal.ReleaseComObject(deviceObject);
                if (enumeratorObject is not null && Marshal.IsComObject(enumeratorObject)) Marshal.ReleaseComObject(enumeratorObject);
            }
        }

        private void ReleaseEndpoint()
        {
            if (endpointObject is not null && Marshal.IsComObject(endpointObject))
            {
                Marshal.ReleaseComObject(endpointObject);
            }
            endpointObject = null;
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                ReleaseEndpoint();
            }
        }
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(nint client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(nint client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint classContext, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(nint notify);
        [PreserveSig] int UnregisterControlChangeNotify(nint notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
