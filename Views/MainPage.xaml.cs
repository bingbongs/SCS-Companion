using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NAudio.CoreAudioApi;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using SCSCompanion.Models;
using SCSCompanion.Services;

namespace SCSCompanion.Views;

public partial class MainPage : Page
{
    private sealed record AudioDeviceChoice(string? Id, string Name)
    {
        public override string ToString() => Name;
    }
    private sealed record ThemeDefinition(
        ElementTheme ElementTheme,
        Windows.UI.Color Window,
        Windows.UI.Color Face,
        Windows.UI.Color Title,
        Windows.UI.Color Surface,
        Windows.UI.Color Border,
        Windows.UI.Color Blue,
        Windows.UI.Color Red,
        Windows.UI.Color Purple,
        Windows.UI.Color Cyan);

    private sealed record ModeDefinition(
        string Name,
        string PhysicalLabel,
        byte Note,
        string Description,
        string[] Submodes,
        Windows.UI.Color Accent);

    private readonly Dictionary<string, ModeDefinition> modes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DJ"] = new("DJ", "FX", 0x20, "Software-specific performance and utility mappings", ["Serato", "Mixxx", "VirtualDJ"], ColorHelper.FromArgb(255, 255, 83, 92)),
        ["Media"] = new("Media", "EQ", 0x26, "System, per-app, microphone, and playback controls", ["System", "Per-app mixer", "Microphone", "Playback"], ColorHelper.FromArgb(255, 66, 214, 220)),
        ["Productivity"] = new("Productivity", "LOOP", 0x22, "Windows, browser, meeting, and streaming shortcuts", ["Windows", "Browser", "Meetings", "Streaming"], ColorHelper.FromArgb(255, 41, 155, 255)),
        ["Looper"] = new("Looper", "LOOP", 0x22, "Four synchronized microphone loop tracks with momentary punch effects", ["Four-track"], ColorHelper.FromArgb(255, 157, 117, 255)),
        ["VRChat"] = new("VRChat", "TRIG", 0x28, "Native OSC inputs and compatibility-aware avatar actions", ["Auto", "Desktop", "PC VR"], ColorHelper.FromArgb(255, 157, 117, 255)),
        ["Mouse"] = new("Mouse", "VINYL", 0x24, "Directional trackpad and rotary pointer profiles", ["Trackpad", "Trackball", "Presentation"], ColorHelper.FromArgb(255, 41, 155, 255)),
        ["Custom"] = new("Custom", "DECK", 0x2A, "User-created mappings and Learn profiles", ["Profile 1", "Profile 2", "Profile 3"], ColorHelper.FromArgb(255, 255, 83, 92)),
        ["Kaoss"] = new("Kaoss", "FX", 0x20, "Scale-locked touch synthesis and sample manipulation", ["Performance"], ColorHelper.FromArgb(255, 255, 70, 181)),
        ["Audacity"] = new("Audacity", "EQ", 0x26, "Fast Audacity transport, edit, and zoom controls", ["Editing"], ColorHelper.FromArgb(255, 255, 205, 45)),
        ["Discord"] = new("Discord", "TRIG", 0x28, "Voice chat mute, deafen, navigation, and volume controls", ["Voice"], ColorHelper.FromArgb(255, 88, 101, 242)),
        ["Simon"] = new("Simon", "DECK", 0x2A, "Infinite four-pad memory game with persistent high score", ["Game"], ColorHelper.FromArgb(255, 70, 230, 130)),
        ["Pink Trombone"] = new("Pink Trombone", "VINYL", 0x24, "Integrated vocal-tract performance synthesizer", ["Vocal tract"], ColorHelper.FromArgb(255, 255, 126, 190)),
    };

    private static readonly Dictionary<byte, string> DefaultModuleAssignments = new()
    {
        [0x20] = "DJ",
        [0x22] = "Looper",
        [0x24] = "Mouse",
        [0x26] = "Media",
        [0x28] = "VRChat",
        [0x2A] = "Custom",
    };

    private static readonly Dictionary<byte, string> SlotLabels = new()
    {
        [0x20] = "FX",
        [0x22] = "LOOP",
        [0x24] = "VINYL",
        [0x26] = "EQ",
        [0x28] = "TRIG",
        [0x2A] = "DECK",
    };

    private readonly Dictionary<byte, string> noteToMode;
    private readonly CustomMappingService customMappingService;
    private readonly DispatcherTimer audioDeviceTimer;
    private string audioRouteSignature = "";
    private bool learningCustomControl;
    private int learnedReleaseNote = -1;
    private bool closed;
    private readonly DispatcherTimer highlightTimer;
    private readonly DispatcherTimer looperLedTimer;
    private readonly DispatcherTimer tempoLedTimer;
    private readonly AppSettingsService settingsService;
    private readonly MidiCaptureService captureService;
    private readonly MidiDeviceService midiService;
    private readonly DjMidiService djMidiService;
    private readonly MediaOutputService mediaOutputService;
    private readonly ProductivityOutputService productivityOutputService;
    private readonly AudioLooperService audioLooperService;
    private readonly VrChatOscService vrChatOscService;
    private readonly MouseTrackpadEngine mouseTrackpadEngine;
    private readonly MixxxProfileInstallerService mixxxProfileInstallerService;
    private readonly VirtualDjProfileInstallerService virtualDjProfileInstallerService;
    private readonly CompanionShortcutService shortcutService;
    private readonly SimonGameService simonService;
    private readonly KaossPerformanceService kaossService;
    private readonly PinkTromboneService pinkTromboneService;
    private SimonState simonState = new("ready", 0, 0, -1, false);
    private readonly List<Ellipse> ringLeds = [];
    private FrameworkElement? highlightedElement;
    private double highlightedOpacity;
    private Brush? highlightedBackground;
    private Brush? highlightedBorderBrush;
    private string activeMode = "Mouse";
    private int activeSubmode;
    private bool routingEnabled = true;
    private int gainLevel = 4;
    private int pitchLevel = 4;
    private int centerLevel = 4;
    private int centerStripX = 64;
    private int ringPosition;
    private bool updatingModuleSelectors;
    private bool looperFlashOn = true;
    private bool tempoPulseOn;
    private AudioPreferences audioPreferences;
    private bool updatingAudioPreferences;
    private static readonly byte[] LedModeNotes = [0x20, 0x22, 0x24, 0x26, 0x28, 0x2A];
    private static readonly byte[] SoftButtonNotes = [0x2C, 0x2E, 0x30, 0x32];
    private static readonly byte[] TransportNotes = [0x6D, 0x6E, 0x6F, 0x70];
    private static readonly HashSet<int> MirroredButtonNotes = [0x2C, 0x2E, 0x30, 0x32, 0x6D, 0x6E, 0x6F, 0x70];

    public ObservableCollection<string> ActivityItems { get; } = [];

    public MainPage()
    {
        InitializeComponent();
        BuildRingLeds();

        settingsService = new AppSettingsService();
        audioPreferences = settingsService.LoadAudioPreferences();
        mixxxProfileInstallerService = new MixxxProfileInstallerService();
        virtualDjProfileInstallerService = new VirtualDjProfileInstallerService();
        RefreshMixxxSetupStatus();
        RefreshVirtualDjSetupStatus();
        var savedTheme = settingsService.LoadTheme();
        ApplyTheme(savedTheme);

        noteToMode = settingsService.LoadModuleAssignments(DefaultModuleAssignments, modes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
        InitializeModuleSelectors();
        ApplyModuleAssignments();
        highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(170) };
        highlightTimer.Tick += OnHighlightTimerTick;
        looperLedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        looperLedTimer.Tick += OnLooperLedTimerTick;
        tempoLedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        tempoLedTimer.Tick += OnTempoLedTimerTick;

        customMappingService = new CustomMappingService();
        customMappingService.ActionReported += OnCompanionActionReported;
        CustomProfileComboBox.SelectedIndex = 0;
        CustomControlComboBox.ItemsSource = CustomMappingService.Controls;
        CustomControlComboBox.SelectedIndex = 0;
        audioDeviceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        audioDeviceTimer.Tick += OnAudioDeviceTimerTick;
        captureService = new MidiCaptureService();
        mouseTrackpadEngine = new MouseTrackpadEngine();
        mouseTrackpadEngine.ActionReported += OnMouseActionReported;
        var savedSensitivity = settingsService.LoadMouseSensitivity();
        mouseTrackpadEngine.Sensitivity = savedSensitivity;
        SensitivitySlider.Value = savedSensitivity;
        UpdateSensitivityLabel(savedSensitivity);
        SensitivitySlider.ValueChanged += OnSensitivityChanged;
        ThemeComboBox.SelectionChanged += OnThemeSelectionChanged;

        djMidiService = new DjMidiService(DispatcherQueue);
        djMidiService.StatusChanged += OnDjMidiStatusChanged;

        mediaOutputService = new MediaOutputService();
        mediaOutputService.ActionReported += OnMediaActionReported;
        mediaOutputService.MicrophoneMuteChanged += OnMicrophoneMuteChanged;

        productivityOutputService = new ProductivityOutputService(mediaOutputService);
        productivityOutputService.ActionReported += OnProductivityActionReported;

        audioLooperService = new AudioLooperService(audioPreferences.InputDeviceId, audioPreferences.OutputDeviceId,
            audioPreferences.SyncCompensationMilliseconds);
        audioLooperService.ActionReported += OnLooperActionReported;
        audioLooperService.StateChanged += OnLooperStateChanged;
        shortcutService = new CompanionShortcutService();
        shortcutService.ActionReported += OnCompanionActionReported;
        simonService = new SimonGameService(settingsService.LoadSimonHighScore());
        simonState = new("ready", 0, simonService.HighScore, -1, false);
        simonService.StateChanged += OnSimonStateChanged;
        kaossService = new KaossPerformanceService(audioPreferences.OutputDeviceId);
        kaossService.StateChanged += OnKaossStateChanged;
        pinkTromboneService = new PinkTromboneService(audioPreferences.OutputDeviceId);
        pinkTromboneService.StateChanged += OnKaossStateChanged;
        tempoLedTimer.Start();
        InitializeAudioSettings();
        audioRouteSignature = GetAudioRouteSignature();
        audioDeviceTimer.Start();

        vrChatOscService = new VrChatOscService();
        vrChatOscService.StatusChanged += OnVrChatOscStatusChanged;
        vrChatOscService.MenuPointerChanged += OnVrChatMenuPointerChanged;

        midiService = new MidiDeviceService(DispatcherQueue);
        midiService.StatusChanged += OnMidiStatusChanged;
        midiService.ActivityReceived += OnMidiActivityReceived;
        midiService.OutputReady += OnMidiOutputReady;
        Unloaded += OnPageUnloaded;

        SelectMode(noteToMode[0x24], cycleIfAlreadyActive: false, source: "Startup");
        midiService.Start();
        djMidiService.Start();
        vrChatOscService.Start();
    }

    private void OnCustomSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (customMappingService is null || CustomProfileComboBox.SelectedIndex < 0 || CustomControlComboBox.SelectedIndex < 0) return;
        CustomChordTextBox.Text = customMappingService.GetChord(CustomProfileComboBox.SelectedIndex, CustomControlComboBox.SelectedIndex);
    }

    private void OnCustomSaveClicked(object sender, RoutedEventArgs e)
    {
        learningCustomControl = false;
        customMappingService.SetChord(CustomProfileComboBox.SelectedIndex, CustomControlComboBox.SelectedIndex, CustomChordTextBox.Text, out var message);
        CustomMappingStatus.Text = message;
        UpdateControlLabels();
    }

    private void OnCustomLearnClicked(object sender, RoutedEventArgs e)
    {
        learningCustomControl = !learningCustomControl;
        CustomMappingStatus.Text = learningCustomControl ? "Press B11–B14 or PLAY/CUE/SYNC/TAP. This press only selects the mapping." : "Learn cancelled.";
    }

    private string GetAudioRouteSignature()
    {
        using var enumerator = new MMDeviceEnumerator();
        string Resolve(string? id, DataFlow flow)
        {
            try { using var device = id is null ? enumerator.GetDefaultAudioEndpoint(flow, Role.Console) : enumerator.GetDevice(id); return $"{device.ID}:{device.State}"; }
            catch { return "unavailable"; }
        }
        return Resolve(audioPreferences.InputDeviceId, DataFlow.Capture) + "|" + Resolve(audioPreferences.OutputDeviceId, DataFlow.Render);
    }

    private void OnAudioDeviceTimerTick(object? sender, object e)
    {
        if (closed) return;
        var signature = GetAudioRouteSignature();
        if (signature == audioRouteSignature) return;
        audioRouteSignature = signature;
        audioLooperService.ConfigureAudioDevices(audioPreferences.InputDeviceId, audioPreferences.OutputDeviceId, audioPreferences.SyncCompensationMilliseconds);
        kaossService.ConfigureOutput(audioPreferences.OutputDeviceId);
        pinkTromboneService.ConfigureOutput(audioPreferences.OutputDeviceId);
        audioLooperService.SetActive(routingEnabled && activeMode == "Looper");
        StatusText.Text = "Audio device change · routing refreshed";
    }

    private void OnModeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string modeName })
        {
            SelectMode(modeName, cycleIfAlreadyActive: true, source: "UI");
        }
    }

    private void InitializeModuleSelectors()
    {
        updatingModuleSelectors = true;
        var moduleNames = modes.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var selector in GetModuleSelectors())
        {
            foreach (var moduleName in moduleNames)
            {
                selector.Items.Add(moduleName);
            }
        }
        updatingModuleSelectors = false;
    }

    private void ApplyModuleAssignments()
    {
        updatingModuleSelectors = true;
        foreach (var (note, moduleName) in noteToMode)
        {
            GetSlotButton(note).Tag = moduleName;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(GetSlotButton(note), $"{SlotLabels[note]} hardware button, {moduleName} mode");
            GetModuleSelector(note).SelectedItem = moduleName;
            GetSlotFunctionText(note).Text = CompactLabel(moduleName);
        }
        updatingModuleSelectors = false;
    }

    private void OnModuleAssignmentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingModuleSelectors || sender is not ComboBox { Tag: string noteText, SelectedItem: string selectedModule } ||
            !byte.TryParse(noteText, out var targetNote))
        {
            return;
        }

        var targetButton = GetSlotButton(targetNote);
        var targetWasActive = targetButton.IsChecked == true;
        var previousModule = noteToMode[targetNote];
        var occupied = noteToMode.FirstOrDefault(item => item.Key != targetNote &&
            string.Equals(item.Value, selectedModule, StringComparison.OrdinalIgnoreCase));
        noteToMode[targetNote] = selectedModule;
        if (occupied.Key != 0)
        {
            noteToMode[occupied.Key] = previousModule;
        }

        ApplyModuleAssignments();
        foreach (var button in GetModeButtons())
        {
            button.IsChecked = string.Equals(button.Tag as string, activeMode, StringComparison.OrdinalIgnoreCase);
        }
        settingsService.SaveModuleAssignments(noteToMode);
        if (targetWasActive)
        {
            SelectMode(selectedModule, cycleIfAlreadyActive: false, source: "Modules");
        }
        else
        {
            UpdateModeFunctionLabels(modes[activeMode].Submodes[activeSubmode]);
            UpdateVisualBank();
            UpdateHardwareModeLeds();
        }
        AddActivity($"{DateTime.Now:HH:mm:ss.fff}  MODULES  {SlotLabels[targetNote]} → {selectedModule}");
    }

    private IEnumerable<ComboBox> GetModuleSelectors()
    {
        yield return FxModuleComboBox;
        yield return LoopModuleComboBox;
        yield return VinylModuleComboBox;
        yield return EqModuleComboBox;
        yield return TrigModuleComboBox;
        yield return DeckModuleComboBox;
    }

    private ComboBox GetModuleSelector(byte note) => note switch
    {
        0x20 => FxModuleComboBox,
        0x22 => LoopModuleComboBox,
        0x24 => VinylModuleComboBox,
        0x26 => EqModuleComboBox,
        0x28 => TrigModuleComboBox,
        _ => DeckModuleComboBox,
    };

    private void InitializeAudioSettings()
    {
        updatingAudioPreferences = true;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            PopulateAudioDeviceSelector(OutputDeviceComboBox, enumerator, DataFlow.Render, audioPreferences.OutputDeviceId);
            PopulateAudioDeviceSelector(InputDeviceComboBox, enumerator, DataFlow.Capture, audioPreferences.InputDeviceId);
            SyncCompensationNumberBox.Value = audioPreferences.SyncCompensationMilliseconds;
            audioPreferences = new AudioPreferences(
                (InputDeviceComboBox.SelectedItem as AudioDeviceChoice)?.Id,
                (OutputDeviceComboBox.SelectedItem as AudioDeviceChoice)?.Id,
                audioPreferences.SyncCompensationMilliseconds);
        }
        finally
        {
            updatingAudioPreferences = false;
        }
        settingsService.SaveAudioPreferences(audioPreferences);
        audioRouteSignature = GetAudioRouteSignature();
    }

    private static void PopulateAudioDeviceSelector(ComboBox selector, MMDeviceEnumerator enumerator, DataFlow flow, string? selectedId)
    {
        selector.Items.Clear();
        string defaultName;
        try
        {
            using var defaultDevice = enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
            defaultName = $"Windows default — {defaultDevice.FriendlyName}";
        }
        catch
        {
            defaultName = "Windows default";
        }
        selector.Items.Add(new AudioDeviceChoice(null, defaultName));
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            selector.Items.Add(new AudioDeviceChoice(device.ID, device.FriendlyName));
            device.Dispose();
        }
        selector.SelectedItem = selector.Items.OfType<AudioDeviceChoice>()
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(selectedId) && item.Id == selectedId) ?? selector.Items[0];
    }

    private void OnAudioDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingAudioPreferences || audioLooperService is null) return;
        ApplyAudioPreferences();
    }

    private void OnSyncCompensationChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (updatingAudioPreferences || audioLooperService is null || double.IsNaN(args.NewValue)) return;
        ApplyAudioPreferences();
    }

    private void ApplyAudioPreferences()
    {
        var inputId = (InputDeviceComboBox.SelectedItem as AudioDeviceChoice)?.Id;
        var outputId = (OutputDeviceComboBox.SelectedItem as AudioDeviceChoice)?.Id;
        var compensation = double.IsNaN(SyncCompensationNumberBox.Value)
            ? 0
            : (int)Math.Round(SyncCompensationNumberBox.Value);
        audioPreferences = new AudioPreferences(inputId, outputId, compensation);
        settingsService.SaveAudioPreferences(audioPreferences);
        audioRouteSignature = GetAudioRouteSignature();
        audioLooperService.ConfigureAudioDevices(inputId, outputId, compensation);
        kaossService.ConfigureOutput(outputId);
        pinkTromboneService.ConfigureOutput(outputId);
        audioLooperService.SetActive(routingEnabled && activeMode == "Looper");
        AddActivity($"{DateTime.Now:HH:mm:ss.fff}  AUDIO    Devices updated · sync {compensation:+0;-0;0} ms");
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem { Tag: string themeName })
        {
            ApplyTheme(themeName);
            settingsService.SaveTheme(themeName);
            AddActivity($"{DateTime.Now:HH:mm:ss.fff}  UI       Theme → {themeName}");
        }
    }

    private void OnSensitivityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        var sensitivity = Math.Round(e.NewValue / 0.05) * 0.05;
        mouseTrackpadEngine.Sensitivity = sensitivity;
        settingsService.SaveMouseSensitivity(sensitivity);
        UpdateSensitivityLabel(sensitivity);
    }

    private void UpdateSensitivityLabel(double sensitivity) =>
        SensitivityValueText.Text = $"{sensitivity * 100:0}%";

    private void OnMixxxInstallClicked(object sender, RoutedEventArgs e)
    {
        var result = mixxxProfileInstallerService.InstallOrRepair();
        MixxxSetupStatusText.Text = result.Message;
        AddActivity($"{DateTime.Now:HH:mm:ss.fff}  MIXXX    {(result.Success ? "Profile installed" : result.Message)}");
        if (result.Success)
        {
            MixxxInstallButton.Content = "Repair / update profile";
        }
    }

    private void RefreshMixxxSetupStatus()
    {
        var status = mixxxProfileInstallerService.Status;
        MixxxSetupStatusText.Text = status;
        MixxxInstallButton.IsEnabled = mixxxProfileInstallerService.CanInstall;
        MixxxInstallButton.Content = status.Contains("profile installed", StringComparison.OrdinalIgnoreCase) &&
                                     !status.Contains("not installed", StringComparison.OrdinalIgnoreCase)
            ? "Repair / update profile"
            : "Install profile";
    }

    private void OnVirtualDjInstallClicked(object sender, RoutedEventArgs e)
    {
        var result = virtualDjProfileInstallerService.InstallOrRepair();
        VirtualDjSetupStatusText.Text = result.Message;
        AddActivity($"{DateTime.Now:HH:mm:ss.fff}  VIRTUALDJ {(result.Success ? "Profile installed" : result.Message)}");
        if (result.Success)
        {
            VirtualDjInstallButton.Content = "Repair / update profile";
        }
    }

    private void RefreshVirtualDjSetupStatus()
    {
        var status = virtualDjProfileInstallerService.Status;
        VirtualDjSetupStatusText.Text = status;
        VirtualDjInstallButton.IsEnabled = virtualDjProfileInstallerService.CanInstall;
        VirtualDjInstallButton.Content = status.Contains("profile installed", StringComparison.OrdinalIgnoreCase) &&
                                         !status.Contains("not installed", StringComparison.OrdinalIgnoreCase)
            ? "Repair / update profile"
            : "Install profile";
    }

    private void ApplyTheme(string themeName)
    {
        var theme = themeName switch
        {
            "Light" => new ThemeDefinition(
                ElementTheme.Light,
                ColorHelper.FromArgb(255, 229, 233, 238), ColorHelper.FromArgb(255, 247, 249, 251),
                ColorHelper.FromArgb(255, 219, 225, 232), ColorHelper.FromArgb(255, 255, 255, 255),
                ColorHelper.FromArgb(255, 82, 94, 108), ColorHelper.FromArgb(255, 0, 101, 204),
                ColorHelper.FromArgb(255, 206, 39, 57), ColorHelper.FromArgb(255, 111, 66, 193),
                ColorHelper.FromArgb(255, 0, 126, 138)),
            "Contrast" => new ThemeDefinition(
                ElementTheme.Dark,
                ColorHelper.FromArgb(255, 0, 0, 0), ColorHelper.FromArgb(255, 0, 0, 0),
                ColorHelper.FromArgb(255, 0, 0, 0), ColorHelper.FromArgb(255, 0, 0, 0),
                ColorHelper.FromArgb(255, 255, 255, 255), ColorHelper.FromArgb(255, 0, 255, 255),
                ColorHelper.FromArgb(255, 255, 230, 0), ColorHelper.FromArgb(255, 255, 0, 255),
                ColorHelper.FromArgb(255, 255, 255, 255)),
            "Ultraviolet" => new ThemeDefinition(
                ElementTheme.Dark,
                ColorHelper.FromArgb(255, 13, 7, 22), ColorHelper.FromArgb(255, 25, 13, 38),
                ColorHelper.FromArgb(255, 34, 17, 51), ColorHelper.FromArgb(255, 8, 4, 14),
                ColorHelper.FromArgb(255, 151, 92, 219), ColorHelper.FromArgb(255, 162, 111, 255),
                ColorHelper.FromArgb(255, 255, 76, 166), ColorHelper.FromArgb(255, 111, 126, 255),
                ColorHelper.FromArgb(255, 71, 231, 220)),
            "Ice" => new ThemeDefinition(
                ElementTheme.Dark,
                ColorHelper.FromArgb(255, 4, 14, 19), ColorHelper.FromArgb(255, 8, 27, 34),
                ColorHelper.FromArgb(255, 8, 37, 47), ColorHelper.FromArgb(255, 2, 12, 16),
                ColorHelper.FromArgb(255, 46, 194, 214), ColorHelper.FromArgb(255, 70, 220, 239),
                ColorHelper.FromArgb(255, 101, 168, 255), ColorHelper.FromArgb(255, 181, 143, 255),
                ColorHelper.FromArgb(255, 225, 250, 255)),
            _ => new ThemeDefinition(
                ElementTheme.Dark,
                ColorHelper.FromArgb(255, 9, 11, 14), ColorHelper.FromArgb(255, 16, 18, 22),
                ColorHelper.FromArgb(255, 24, 26, 30), ColorHelper.FromArgb(255, 5, 7, 10),
                ColorHelper.FromArgb(255, 83, 90, 100), ColorHelper.FromArgb(255, 41, 155, 255),
                ColorHelper.FromArgb(255, 255, 83, 92), ColorHelper.FromArgb(255, 157, 117, 255),
                ColorHelper.FromArgb(255, 66, 214, 220)),
        };

        RequestedTheme = theme.ElementTheme;
        SetBrush("DeviceWindowBrush", theme.Window);
        SetBrush("DeviceFaceBrush", theme.Face);
        SetBrush("DeviceTitleBrush", theme.Title);
        SetBrush("DeviceSurfaceBrush", theme.Surface);
        SetBrush("DeviceBorderBrush", theme.Border);
        SetBrush("StantonBlueBrush", theme.Blue);
        SetBrush("StantonRedBrush", theme.Red);
        SetBrush("StantonPurpleBrush", theme.Purple);
        SetBrush("StantonCyanBrush", theme.Cyan);

        var normalizedTheme = themeName is "Light" or "Contrast" or "Ultraviolet" or "Ice" ? themeName : "Stanton";
        ThemeComboBox.SelectedItem = ThemeComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, normalizedTheme, StringComparison.Ordinal));
        if (ringLeds.Count > 0)
        {
            UpdateVisualBank();
        }
    }

    private static void SetBrush(string key, Windows.UI.Color color)
    {
        if (Application.Current.Resources[key] is SolidColorBrush brush)
        {
            brush.Color = color;
        }
    }

    private void SelectMode(string modeName, bool cycleIfAlreadyActive, string source)
    {
        if (!modes.TryGetValue(modeName, out var mode))
        {
            return;
        }

        customMappingService.ReleaseAll();
        mouseTrackpadEngine.ReleaseAll();
        djMidiService.ReleaseAll();
        productivityOutputService.ReleaseAll();
        audioLooperService.SetActive(false);
        kaossService.SetActive(false);
        pinkTromboneService.SetActive(false);
        shortcutService.ReleaseAll();
        if (activeMode == "Simon") simonService.Stop();
        looperLedTimer.Stop();
        looperFlashOn = true;
        vrChatOscService.ReleaseAll();

        if (activeMode.Equals(modeName, StringComparison.OrdinalIgnoreCase) && cycleIfAlreadyActive)
        {
            activeSubmode = (activeSubmode + 1) % mode.Submodes.Length;
        }
        else
        {
            activeMode = modeName;
            activeSubmode = 0;
        }
        audioLooperService.SetActive(routingEnabled && activeMode == "Looper");
        kaossService.SetActive(routingEnabled && activeMode == "Kaoss");
        pinkTromboneService.SetActive(routingEnabled && activeMode == "Pink Trombone");

        foreach (var button in GetModeButtons())
        {
            button.IsChecked = string.Equals(button.Tag as string, activeMode, StringComparison.OrdinalIgnoreCase);
        }

        var submode = mode.Submodes[activeSubmode];
        if ((modeName == "Media" && submode == "Microphone") ||
            (modeName == "Productivity" && submode == "Meetings"))
        {
            mediaOutputService.RefreshMicrophoneState();
        }
        ActiveModeText.Text = $"{mode.Name} · {submode}";
        ActiveModeDescription.Text = mode.Description;
        ActiveModeAccent.Background = new SolidColorBrush(mode.Accent);
        var slotLabel = GetPhysicalLabelForMode(modeName);
        StatusText.Text = $"{slotLabel} → {mode.Name} → {submode}";
        UpdateModeFunctionLabels(submode);
        UpdateControlLabels();
        UpdateModuleOverlay();
        UpdateVisualBank();
        UpdateRoutingIndicator();
        UpdateHardwareModeLeds();
        AddActivity($"{DateTime.Now:HH:mm:ss.fff}  {source,-8} {slotLabel} → {mode.Name} / {submode}");
    }

    private string GetPhysicalLabelForMode(string modeName)
    {
        var assignedSlot = noteToMode.FirstOrDefault(item =>
            string.Equals(item.Value, modeName, StringComparison.OrdinalIgnoreCase));
        return assignedSlot.Key != 0 ? SlotLabels[assignedSlot.Key] : "MODULE";
    }

    private void UpdateControlLabels()
    {
        var looper = activeMode == "Looper";
        var kaoss = activeMode == "Kaoss";
        var pink = activeMode == "Pink Trombone";
        GainHeaderText.Text = looper ? "OUT" : kaoss || pink ? "LEVEL" : "GAIN";
        PitchHeaderText.Text = looper || kaoss ? "BPM" : pink ? "VOICE" : "PITCH";
        SurfaceTitleText.Text = looper ? audioLooperService.IsShiftHeld ? "LIVE FX DEPTH" : "FX DEPTH" : kaoss ? "PITCH × TIMBRE" : pink ? "TONGUE × MOUTH" : "STANTOUCH";
        EffectTopLabel.Visibility = looper ? Visibility.Visible : Visibility.Collapsed;
        EffectRightLabel.Visibility = looper ? Visibility.Visible : Visibility.Collapsed;
        EffectBottomLabel.Visibility = looper ? Visibility.Visible : Visibility.Collapsed;
        EffectLeftLabel.Visibility = looper ? Visibility.Visible : Visibility.Collapsed;
        foreach (var label in new[] { Track1FxLabel, Track2FxLabel, Track3FxLabel, Track4FxLabel })
        {
            label.Visibility = looper ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!looper)
        {
            PlayButtonText.Text = "PLAY";
            CueButtonText.Text = "CUE";
            SyncButtonText.Text = "SYNC";
            TapButtonText.Text = "TAP";
            if (kaoss)
            {
                PlayButtonText.Text = "LOW"; CueButtonText.Text = "MID"; SyncButtonText.Text = "HIGH"; TapButtonText.Text = "TOP";
                SoftButton1Text.Text = kaossService.Hold ? "HOLD ON" : "HOLD"; SoftButton2Text.Text = kaossService.GateArp ? "ARP ON" : "GATE ARP";
                SoftButton3Text.Text = $"TAP\n{kaossService.Bpm:0}"; SoftButton4Text.Text = $"NEXT\n{kaossService.ProgramName}";
            }
            else if (pink)
            {
                PlayButtonText.Text = "CHEST"; CueButtonText.Text = "BRIGHT"; SyncButtonText.Text = "WHISPER"; TapButtonText.Text = "ROBOT";
                SoftButton1Text.Text = "HOLD"; SoftButton2Text.Text = "NASAL"; SoftButton3Text.Text = "PITCH -"; SoftButton4Text.Text = "PITCH +";
            }
            else if (activeMode == "Audacity")
            {
                PlayButtonText.Text = "RECORD"; CueButtonText.Text = "PLAY"; SyncButtonText.Text = "PAUSE"; TapButtonText.Text = "STOP";
                SoftButton1Text.Text = "UNDO"; SoftButton2Text.Text = "REDO"; SoftButton3Text.Text = "ZOOM +"; SoftButton4Text.Text = "ZOOM -";
            }
            else if (activeMode == "Discord")
            {
                PlayButtonText.Text = "MUTE"; CueButtonText.Text = "DEAFEN"; SyncButtonText.Text = "VOICE"; TapButtonText.Text = "SHORTCUTS";
                SoftButton1Text.Text = "NEXT"; SoftButton2Text.Text = "PREV"; SoftButton3Text.Text = "SEARCH"; SoftButton4Text.Text = "DISMISS";
            }
            else if (activeMode == "Simon")
            {
                PlayButtonText.Text = "BLUE"; CueButtonText.Text = "RED"; SyncButtonText.Text = "PURPLE"; TapButtonText.Text = "CYAN";
                SoftButton1Text.Text = simonService.IsPlaying ? "PLAYING" : "START"; SoftButton2Text.Text = ""; SoftButton3Text.Text = $"SCORE\n{simonState.Score}"; SoftButton4Text.Text = $"BEST\n{simonState.HighScore}";
            }
            else if (activeMode == "Custom")
            {
                var customLabels = new[] { SoftButton1Text, SoftButton2Text, SoftButton3Text, SoftButton4Text, PlayButtonText, CueButtonText, SyncButtonText, TapButtonText };
                for (var i = 0; i < customLabels.Length; i++)
                {
                    var chord = customMappingService.GetChord(activeSubmode, i);
                    customLabels[i].Text = string.IsNullOrEmpty(chord) ? "SETUP" : (chord.Length > 12 ? chord[..11] + "…" : chord).ToUpperInvariant();
                    ToolTipService.SetToolTip(customLabels[i], string.IsNullOrEmpty(chord) ? "Assign in Settings → Custom" : chord);
                }
            }
            else if (activeMode == "Media" && modes[activeMode].Submodes[activeSubmode] == "Per-app mixer")
            {
                SoftButton1Text.Text = "PREV APP"; SoftButton2Text.Text = "NEXT APP";
                SoftButton3Text.Text = "LEVEL −"; SoftButton4Text.Text = "LEVEL +";
                PlayButtonText.Text = "MUTE"; TapButtonText.Text = "MUTE";
            }
            else if (activeMode == "Productivity" && modes[activeMode].Submodes[activeSubmode] == "Streaming")
            {
                SoftButton1Text.Text = "F13"; SoftButton2Text.Text = "F14"; SoftButton3Text.Text = "F15"; SoftButton4Text.Text = "F16";
                PlayButtonText.Text = "F17"; CueButtonText.Text = "F18"; SyncButtonText.Text = "F19"; TapButtonText.Text = "F20";
                SurfaceTitleText.Text = "CTRL + ALT";
            }
            else if (activeMode == "Mouse" && modes[activeMode].Submodes[activeSubmode] == "Presentation")
            {
                SoftButton1Text.Text = "PREV"; SoftButton2Text.Text = "NEXT"; SoftButton3Text.Text = "BLACK"; SoftButton4Text.Text = "WHITE";
                PlayButtonText.Text = "START"; CueButtonText.Text = "EXIT"; SyncButtonText.Text = "PREV"; TapButtonText.Text = "NEXT";
            }
            else if (activeMode == "Mouse")
            {
                SoftButton1Text.Text = "LEFT"; SoftButton2Text.Text = "RIGHT"; SoftButton3Text.Text = mouseTrackpadEngine.IsMacroRecording ? "REC..." : mouseTrackpadEngine.HasMacro ? "RE-REC" : "REC"; SoftButton4Text.Text = "BACK";
                PlayButtonText.Text = mouseTrackpadEngine.IsMacroPlaying ? "STOP MACRO" : mouseTrackpadEngine.HasMacro ? "PLAY MACRO" : "EMPTY";
            }
            else { SoftButton1Text.Text = "B11"; SoftButton2Text.Text = "B12"; SoftButton3Text.Text = "B13"; SoftButton4Text.Text = "B14"; }
            return;
        }

        var labels = new[] { PlayButtonText, CueButtonText, SyncButtonText, TapButtonText };
        for (var index = 0; index < labels.Length; index++)
        {
            var action = !audioLooperService.HasTrackAudio(index)
                ? "REC"
                : audioLooperService.GetTrackState(index) == "STOPPED" ? "PLAY" : "STOP";
            if (audioLooperService.IsRecording && audioLooperService.RecordingTrack == index) action = "STOP";
            if (audioLooperService.IsFinalizing && audioLooperService.RecordingTrack == index) action = "WAIT";
            if (audioLooperService.EraseArmedTrack == index) action = "ERASE";
            labels[index].Text = $"T{index + 1} {action}";
        }
        var fxLabels = new[] { Track1FxLabel, Track2FxLabel, Track3FxLabel, Track4FxLabel };
        for (var index = 0; index < fxLabels.Length; index++)
        {
            var enabled = audioLooperService.IsTrackFxEnabled(index);
            fxLabels[index].Text = $"T{index + 1} FX {(enabled ? "ON" : "OFF")}";
            fxLabels[index].Opacity = enabled ? 1d : 0.45d;
        }
        SoftButton1Text.Text = audioLooperService.IsPunchActive
            ? audioLooperService.IsPunchHeld ? "RELEASE" : "HOLD"
            : audioLooperService.IsDubMode ? "END DUB" : "DUB";
        SoftButton2Text.Text = "PLAY ALL";
        SoftButton3Text.Text = audioLooperService.IsShiftHeld
            ? "SHIFT"
            : audioLooperService.IsDubMode ? audioLooperService.CanUndo ? "UNDO" : "UNDO\nWAIT" : $"TAP\n{audioLooperService.Bpm:0}";
        SoftButton4Text.Text = audioLooperService.IsShiftHeld
            ? $"NEXT FX\n{audioLooperService.LiveEffectName}"
            : audioLooperService.LiveEffectName == "OFF" ? "STOP ALL" : $"LIVE\n{audioLooperService.LiveEffectName}";
    }

    private IEnumerable<ToggleButton> GetModeButtons()
    {
        yield return DjModeButton;
        yield return MediaModeButton;
        yield return ProductivityModeButton;
        yield return VrChatModeButton;
        yield return MouseModeButton;
        yield return CustomModeButton;
    }

    private void OnMidiStatusChanged(object? sender, string status)
    {
        var connected = status.StartsWith("Connected", StringComparison.OrdinalIgnoreCase);
        ConnectionText.Text = connected
            ? status.Contains("LEDs ready", StringComparison.OrdinalIgnoreCase) ? "SCS.3d · LED" : "SCS.3d"
            : status.Contains("disconnected", StringComparison.OrdinalIgnoreCase) ? "Offline" : "Finding…";
        ToolTipService.SetToolTip(ConnectionText, status);
        ConnectionDot.Fill = new SolidColorBrush(connected ? ColorHelper.FromArgb(255, 66, 214, 220) : ColorHelper.FromArgb(255, 255, 83, 92));
        if (connected)
        {
            audioLooperService.SetActive(routingEnabled && activeMode == "Looper");
            kaossService.SetActive(routingEnabled && activeMode == "Kaoss");
            pinkTromboneService.SetActive(routingEnabled && activeMode == "Pink Trombone");
        }
        else
        {
            customMappingService.ReleaseAll();
            mouseTrackpadEngine.ReleaseAll();
            djMidiService.ReleaseAll();
            productivityOutputService.ReleaseAll();
            audioLooperService.SetActive(false);
            kaossService.SetActive(false);
            pinkTromboneService.SetActive(false);
            shortcutService.ReleaseAll();
            simonService.Stop();
            vrChatOscService.ReleaseAll();
            AddActivity($"{DateTime.Now:HH:mm:ss.fff}  DEVICE   {status}");
        }
    }

    private void OnMidiOutputReady(object? sender, EventArgs e)
    {
        UpdateHardwareModeLeds();
        RefreshHardwareSurfaceFeedback();
        AddActivity($"{DateTime.Now:HH:mm:ss.fff}  LED      Hardware feedback ready");
    }

    private void OnMidiActivityReceived(object? sender, MidiActivity activity)
    {
        if (closed) return;
        if (activity.Kind == "Note off" && activity.Data1 == learnedReleaseNote)
        {
            learnedReleaseNote = -1;
            return;
        }
        if (learningCustomControl && activity.Kind == "Note on" && Array.IndexOf(CustomMappingService.Notes, activity.Data1) is var learned && learned >= 0)
        {
            learningCustomControl = false;
            learnedReleaseNote = activity.Data1;
            CustomControlComboBox.SelectedIndex = learned;
            CustomMappingStatus.Text = $"Selected {CustomMappingService.Controls[learned]}. Enter a shortcut and Save.";
            return;
        }
        captureService.Record(activity);
        AddActivity($"{activity.Timestamp:HH:mm:ss.fff}  MIDI CH{activity.Channel:D2}  {activity.Description,-26} [{activity.RawHex}]");
        LastTouchedText.Text = activity.Description;

        if (activity.Kind == "Note on" && noteToMode.TryGetValue((byte)activity.Data1, out var selectedMode))
        {
            SelectMode(selectedMode, cycleIfAlreadyActive: true, source: "Hardware");
            Highlight(GetModeButton(selectedMode));
            return;
        }

        var submode = modes[activeMode].Submodes[activeSubmode];
        djMidiService.Handle(activity, routingEnabled && activeMode == "DJ", submode);
        mediaOutputService.Handle(activity, routingEnabled && activeMode == "Media", submode);
        productivityOutputService.Handle(activity, routingEnabled && activeMode == "Productivity", submode);
        audioLooperService.Handle(activity, routingEnabled && activeMode == "Looper");
        kaossService.Handle(activity, routingEnabled && activeMode == "Kaoss");
        pinkTromboneService.Handle(activity, routingEnabled && activeMode == "Pink Trombone");
        shortcutService.Handle(activity, routingEnabled && activeMode is "Audacity" or "Discord", activeMode);
        simonService.Handle(activity, routingEnabled && activeMode == "Simon");
        customMappingService.Handle(activity, routingEnabled && activeMode == "Custom", activeSubmode);
        vrChatOscService.Handle(activity, routingEnabled && activeMode == "VRChat", submode);
        var menuMouse = routingEnabled && activeMode == "VRChat" && vrChatOscService.MenuPointerActive;
        if (routingEnabled && activeMode == "Mouse")
        {
            mouseTrackpadEngine.Handle(activity, enabled: true, submode: submode);
        }
        else if (menuMouse && activity.Data1 is 0x62 or 0x63)
        {
            mouseTrackpadEngine.Handle(activity, enabled: true, submode: "Trackpad");
        }

        MirrorHardwareButtonLed(activity);
        MirrorSegmentedFeedback(activity);
        UpdateLiveSurfaceFeedback(activity);
        if (activeMode == "Looper" && activity.Kind == "Note off" && activity.Data1 == 0x62)
        {
            ringPosition = -1;
            _ = midiService.SendControlChange(0x62, 0x00);
            RestoreHighlight();
            UpdateVisualBank();
        }
        else
        {
            Highlight(GetHardwareElement(activity));
        }
    }

    private void UpdateHardwareModeLeds()
    {
        if (!midiService.IsOutputReady)
        {
            return;
        }

        var activeNote = noteToMode.FirstOrDefault(item =>
            string.Equals(item.Value, activeMode, StringComparison.OrdinalIgnoreCase)).Key;
        var bankVelocity = GetBankVelocity();
        var activeVelocity = GetActiveVelocity();
        foreach (var note in LedModeNotes)
        {
            _ = midiService.SendNote(note, routingEnabled && note == activeNote ? activeVelocity : bankVelocity);
        }

        foreach (var note in SoftButtonNotes)
        {
            var latched = IsButtonLatched(note);
            var tempoPulse = activeMode == "Looper" && note == 0x30 && tempoPulseOn;
            _ = midiService.SendNote((byte)note, routingEnabled ? latched || tempoPulse ? activeVelocity : bankVelocity : (byte)0x00);
        }
        foreach (var note in TransportNotes)
        {
            _ = midiService.SendNote(note, routingEnabled ? GetTransportLedValue(note) : (byte)0x00);
        }
    }

    private bool IsMicrophoneMuteBank()
    {
        var submode = modes[activeMode].Submodes[activeSubmode];
        return (activeMode == "Media" && submode == "Microphone") ||
               (activeMode == "Productivity" && submode == "Meetings");
    }

    private bool IsButtonLatched(int note)
    {
        if (IsMicrophoneMuteBank() && note == 0x30 && mediaOutputService.IsMicrophoneMuted) return true;
        if (activeMode == "Kaoss" && note == 0x2C && kaossService.Hold) return true;
        if (activeMode == "Kaoss" && note == 0x2E && kaossService.GateArp) return true;
        if (activeMode == "Mouse" && note == 0x30 && mouseTrackpadEngine.IsMacroRecording) return true;
        if (activeMode != "Looper") return false;
        if (note == 0x2C && (audioLooperService.IsRecording || audioLooperService.IsDubMode || audioLooperService.IsPunchHeld)) return true;
        if (note == 0x30 && (audioLooperService.IsShiftHeld || audioLooperService.IsDubMode)) return true;
        if (note == 0x32 && audioLooperService.LiveEffectName != "OFF") return true;
        return false;
    }

    private byte GetTransportLedValue(int note)
    {
        if (activeMode == "Simon" && simonState.Pad == note - 0x6D && simonState.Lit) return 0x01;
        if (activeMode == "Mouse" && note == 0x6D && mouseTrackpadEngine.IsMacroPlaying) return 0x01;
        if (activeMode != "Looper") return 0x00; // Transport family: 0=blue, 1=red.
        var trackIndex = note - 0x6D;
        if (trackIndex is < 0 or > 3) return 0x00;
        if (audioLooperService.EraseArmedTrack == trackIndex)
        {
            return looperFlashOn ? (byte)0x01 : (byte)0x00;
        }
        if (audioLooperService.IsRecording && audioLooperService.RecordingTrack == trackIndex)
        {
            return looperFlashOn ? (byte)0x01 : (byte)0x00;
        }
        return audioLooperService.HasTrackAudio(trackIndex) ? (byte)0x01 : (byte)0x00;
    }

    private void MirrorHardwareButtonLed(MidiActivity activity)
    {
        if (activity.Kind is not ("Note on" or "Note off") || !MirroredButtonNotes.Contains(activity.Data1))
        {
            return;
        }

        if (activity.Data1 is >= 0x6D and <= 0x70)
        {
            var transportValue = !routingEnabled
                ? (byte)0x00
                : activeMode == "Looper"
                    ? activity.Kind == "Note on" ? (byte)0x01 : GetTransportLedValue(activity.Data1)
                    : activity.Kind == "Note on" ? (byte)0x01 : (byte)0x00;
            _ = midiService.SendNote((byte)activity.Data1, transportValue);
            return;
        }

        var latched = IsButtonLatched(activity.Data1);
        _ = midiService.SendNote((byte)activity.Data1,
            activity.Kind == "Note on" ? GetActiveVelocity() : routingEnabled ? latched || (activeMode == "Looper" && activity.Data1 == 0x30 && tempoPulseOn) ? GetActiveVelocity() : GetBankVelocity() : (byte)0x00);
    }

    private void MirrorSegmentedFeedback(MidiActivity activity)
    {
        if (activity.Kind != "Control" || !midiService.IsOutputReady)
        {
            return;
        }

        var level = Math.Clamp((int)Math.Round(activity.Data2 / 127d * 9d), 0, 9);
        switch (activity.Data1)
        {
            case 0x07:
                gainLevel = level;
                _ = midiService.SendControlChange(0x07, (byte)(0x28 + level));
                break;
            case 0x03:
                pitchLevel = level;
                _ = midiService.SendControlChange(0x03, (byte)(0x14 + level));
                break;
            case 0x01:
                if (centerStripX is >= 42 and <= 86)
                {
                    centerLevel = level;
                    _ = midiService.SendControlChange(0x01, (byte)Math.Min(level, 8));
                }
                break;
            case 0x02:
                centerStripX = activity.Data2;
                break;
            case 0x62:
                ringPosition = Math.Clamp(activity.Data2 / 8, 0, 15);
                _ = midiService.SendControlChange(0x62, (byte)(ringPosition + 1));
                break;
        }
    }

    private void RefreshHardwareSurfaceFeedback()
    {
        if (!midiService.IsOutputReady)
        {
            return;
        }

        _ = midiService.SendControlChange(0x07, (byte)(0x28 + gainLevel));
        _ = midiService.SendControlChange(0x03, (byte)(0x14 + pitchLevel));
        _ = midiService.SendControlChange(0x01, (byte)Math.Min(centerLevel, 8));
        _ = midiService.SendControlChange(0x62, (byte)Math.Max(0, ringPosition + 1));
    }

    private void OnMouseActionReported(object? sender, string action)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            LastTouchedText.Text = $"→ {action}";
            AddActivity($"{DateTime.Now:HH:mm:ss.fff}  OUTPUT   {action}");
            if (activeMode == "Mouse") { UpdateControlLabels(); UpdateVisualBank(); UpdateHardwareModeLeds(); }
        });
    }

    private void OnCompanionActionReported(object? sender, string action)
    {
        LastTouchedText.Text = $"→ {action}";
        AddActivity($"{DateTime.Now:HH:mm:ss.fff}  {activeMode.ToUpperInvariant(),-8} {action}");
    }

    private void OnKaossStateChanged(object? sender, string state)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            LastTouchedText.Text = state;
            KaossSampleText.Text = kaossService.SampleName;
            UpdateControlLabels(); UpdateVisualBank(); UpdateHardwareModeLeds(); UpdateRoutingIndicator();
            AddActivity($"{DateTime.Now:HH:mm:ss.fff}  {activeMode.ToUpperInvariant(),-8} {state}");
        });
    }

    private void OnSimonStateChanged(object? sender, SimonState state)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            simonState = state;
            GameStatusText.Text = state.Phase switch { "lost" => "YOU LOSE", "watch" or "flash" => "WATCH", "repeat" or "input" => "YOUR TURN", _ => "PRESS START" };
            GameScoreText.Text = $"SCORE {state.Score} · BEST {state.HighScore}";
            if (state.Phase == "lost") settingsService.SaveSimonHighScore(state.HighScore);
            if (midiService.IsOutputReady && state.Pad is >= 0 and < 4)
                _ = midiService.SendNote((byte)(0x6D + state.Pad), state.Lit ? (byte)0x01 : (byte)0x00);
            UpdateControlLabels(); UpdateVisualBank();
            if (state.Phase == "lost") AddActivity($"{DateTime.Now:HH:mm:ss.fff}  SIMON    Lost at {state.Score} · best {state.HighScore}");
        });
    }

    private void UpdateModuleOverlay()
    {
        KaossDropTarget.Visibility = activeMode == "Kaoss" ? Visibility.Visible : Visibility.Collapsed;
        GameStatusOverlay.Visibility = activeMode == "Simon" ? Visibility.Visible : Visibility.Collapsed;
        KaossSampleText.Text = kaossService.SampleName;
        GameStatusText.Text = simonState.Phase == "lost" ? "YOU LOSE" : simonService.IsPlaying ? "WATCH / REPEAT" : "PRESS START";
        GameScoreText.Text = $"SCORE {simonState.Score} · BEST {simonState.HighScore}";
    }

    private void OnKaossDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Load sample into Kaoss";
        e.DragUIOverride.IsContentVisible = true;
    }

    private async void OnKaossDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.FirstOrDefault() is not StorageFile file) return;
        var extension = file.FileType.ToLowerInvariant();
        if (extension is not (".mp3" or ".wav" or ".flac" or ".ogg" or ".aif" or ".aiff"))
        {
            LastTouchedText.Text = "Unsupported audio file"; return;
        }
        KaossSampleText.Text = "LOADING…";
        await kaossService.LoadSampleAsync(file.Path);
    }

    private void OnDjMidiStatusChanged(object? sender, string status)
    {
        UpdateRoutingIndicator();
        if (!status.StartsWith("Looking", StringComparison.OrdinalIgnoreCase))
        {
            AddActivity($"{DateTime.Now:HH:mm:ss.fff}  DJ MIDI  {status}");
        }
    }

    private void OnMediaActionReported(object? sender, string action)
    {
        LastTouchedText.Text = $"→ {action}";
        AddActivity($"{DateTime.Now:HH:mm:ss.fff}  MEDIA    {action}");
    }

    private void OnMicrophoneMuteChanged(object? sender, bool muted)
    {
        UpdateVisualBank();
        UpdateHardwareModeLeds();
    }

    private void OnProductivityActionReported(object? sender, string action)
    {
        LastTouchedText.Text = $"→ {action}";
        AddActivity($"{DateTime.Now:HH:mm:ss.fff}  PRODUCT  {action}");
    }

    private void OnLooperActionReported(object? sender, string action)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            LastTouchedText.Text = $"→ {action}";
            AddActivity($"{DateTime.Now:HH:mm:ss.fff}  LOOPER   {action}");
        });
    }

    private void OnLooperStateChanged(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (activeMode == "Looper")
            {
                if (audioLooperService.EraseArmedTrack >= 0)
                {
                    looperFlashOn = true;
                    looperLedTimer.Interval = TimeSpan.FromMilliseconds(90);
                    looperLedTimer.Start();
                }
                else if (audioLooperService.IsRecording)
                {
                    looperFlashOn = true;
                    looperLedTimer.Interval = TimeSpan.FromMilliseconds(280);
                    looperLedTimer.Start();
                }
                else
                {
                    looperLedTimer.Stop();
                    looperFlashOn = true;
                }
                UpdateControlLabels();
                UpdateVisualBank();
                UpdateHardwareModeLeds();
                UpdateRoutingIndicator();
            }
        });
    }

    private void OnLooperLedTimerTick(object? sender, object e)
    {
        if (activeMode != "Looper" || (!audioLooperService.IsRecording && audioLooperService.EraseArmedTrack < 0))
        {
            looperLedTimer.Stop();
            looperFlashOn = true;
            return;
        }
        looperFlashOn = !looperFlashOn;
        UpdateVisualBank();
        UpdateHardwareModeLeds();
    }

    private void OnTempoLedTimerTick(object? sender, object e)
    {
        LooperInputMeter.Visibility = activeMode == "Looper" ? Visibility.Visible : Visibility.Collapsed;
        if (activeMode == "Looper") LooperInputMeter.Value = audioLooperService.InputPeak * 100;
        var showTempo = activeMode == "Looper" && !audioLooperService.IsShiftHeld && !audioLooperService.IsDubMode;
        var nextPulse = showTempo && audioLooperService.BeatPhase < 0.16d;
        if (nextPulse == tempoPulseOn) return;
        tempoPulseOn = nextPulse;
        if (activeMode == "Looper")
        {
            UpdateVisualBank();
            if (midiService.IsOutputReady)
            {
                _ = midiService.SendNote(0x30, tempoPulseOn ? GetActiveVelocity() : IsButtonLatched(0x30) ? GetActiveVelocity() : GetBankVelocity());
            }
        }
    }

    private void OnVrChatOscStatusChanged(object? sender, string status)
    {
        VrChatOscStatusText.Text = status;
        UpdateRoutingIndicator();
    }

    private void OnVrChatMenuPointerChanged(object? sender, bool active)
    {
        if (!active)
        {
            customMappingService.ReleaseAll();
            mouseTrackpadEngine.ReleaseAll();
        }

        LastTouchedText.Text = active ? "B14 · RADIAL MOUSE" : "RADIAL MOUSE OFF";
        UpdateRoutingIndicator();
        UpdateVisualBank();
        AddActivity($"{DateTime.Now:HH:mm:ss.fff}  VRCHAT  Radial mouse {(active ? "enabled" : "released")}");
    }

    private void OnRoutingToggleClicked(object sender, RoutedEventArgs e)
    {
        routingEnabled = !routingEnabled;
        if (!routingEnabled)
        {
            customMappingService.ReleaseAll();
            mouseTrackpadEngine.ReleaseAll();
            djMidiService.ReleaseAll();
            productivityOutputService.ReleaseAll();
            audioLooperService.SetActive(false);
            kaossService.SetActive(false);
            pinkTromboneService.SetActive(false);
            shortcutService.ReleaseAll();
            simonService.Stop();
            vrChatOscService.ReleaseAll();
        }
        else
        {
            audioLooperService.SetActive(activeMode == "Looper");
            kaossService.SetActive(activeMode == "Kaoss");
            pinkTromboneService.SetActive(activeMode == "Pink Trombone");
        }

        UpdateRoutingIndicator();
        UpdateHardwareModeLeds();
        AddActivity($"{DateTime.Now:HH:mm:ss.fff}  ROUTING  {(routingEnabled ? "Enabled" : "Paused")}");
    }

    private void UpdateRoutingIndicator()
    {
        var submode = modes[activeMode].Submodes[activeSubmode];
        var mouseLive = routingEnabled && activeMode == "Mouse" && MouseTrackpadEngine.SupportsSubmode(submode);
        var djLive = routingEnabled && activeMode == "DJ" && DjMidiService.SupportsSubmode(submode) && djMidiService.IsReady;
        var mediaLive = routingEnabled && activeMode == "Media" && MediaOutputService.SupportsSubmode(submode);
        var productivityLive = routingEnabled && activeMode == "Productivity" && ProductivityOutputService.SupportsSubmode(submode);
        var looperLive = routingEnabled && activeMode == "Looper";
        var vrLive = routingEnabled && activeMode == "VRChat" && VrChatOscService.SupportsSubmode(submode);
        var kaossLive = routingEnabled && activeMode == "Kaoss";
        var pinkLive = routingEnabled && activeMode == "Pink Trombone";
        var shortcutLive = routingEnabled && activeMode is "Audacity" or "Discord";
        var customLive = routingEnabled && activeMode == "Custom";
        var simonLive = routingEnabled && activeMode == "Simon";
        var menuMouse = vrLive && vrChatOscService.MenuPointerActive;
        var productivityLabel = submode switch
        {
            "Browser" => "BROWSER",
            "Meetings" => "MEETING",
            _ => "WINDOWS",
        };
        var looperLabel = audioLooperService.IsRecording
            ? $"REC T{audioLooperService.RecordingTrack + 1}"
            : audioLooperService.LiveEffectName != "OFF"
                ? $"LIVE {audioLooperService.LiveEffectName} · {audioLooperService.Bpm:0} BPM"
                : $"T{audioLooperService.SelectedTrack + 1} · {audioLooperService.GetTrackSteps(audioLooperService.SelectedTrack)} STEP · {audioLooperService.Bpm:0} BPM";
        RoutingStateText.Text = !routingEnabled ? "PAUSED" : menuMouse ? "VR MENU MOUSE" : mouseLive ? "MOUSE LIVE" : djLive ? "DJ MIDI" : mediaLive ? "MEDIA" : looperLive ? looperLabel : kaossLive ? $"KAOSS · {kaossService.ProgramName}" : pinkLive ? "VOCAL TRACT" : shortcutLive ? activeMode.ToUpperInvariant() : customLive ? "CUSTOM LIVE" : simonLive ? $"SIMON · {simonState.Score}" : productivityLive ? productivityLabel : vrLive ? "VR OSC" : "OBSERVE";
        RoutingDot.Fill = new SolidColorBrush(!routingEnabled
            ? ColorHelper.FromArgb(255, 255, 83, 92)
            : mouseLive || djLive || mediaLive || looperLive || kaossLive || pinkLive || shortcutLive || simonLive || customLive || productivityLive || vrLive
                ? ColorHelper.FromArgb(255, 66, 214, 220)
                : ColorHelper.FromArgb(255, 130, 136, 145));
    }

    private byte GetBankVelocity() => (byte)((activeSubmode % 3) switch
    {
        0 => 0x02, // blue
        1 => 0x03, // purple
        _ => 0x01, // red
    });

    private byte GetActiveVelocity() => (byte)((activeSubmode % 3) switch
    {
        0 => 0x03, // purple against blue
        1 => 0x01, // red against purple
        _ => 0x02, // blue against red
    });

    private SolidColorBrush CreateLedBrush(byte velocity, byte alpha = 255)
    {
        var resourceName = velocity switch
        {
            0x01 => "StantonRedBrush",
            0x03 => "StantonPurpleBrush",
            _ => "StantonBlueBrush",
        };
        var source = (SolidColorBrush)Application.Current.Resources[resourceName];
        return new SolidColorBrush(ColorHelper.FromArgb(alpha, source.Color.R, source.Color.G, source.Color.B));
    }

    private void UpdateModeFunctionLabels(string activeSubmodeName)
    {
        foreach (var (note, moduleName) in noteToMode)
        {
            GetSlotFunctionText(note).Text = CompactLabel(moduleName);
        }

        var activeSlot = noteToMode.FirstOrDefault(item =>
            string.Equals(item.Value, activeMode, StringComparison.OrdinalIgnoreCase));
        if (activeSlot.Key != 0)
        {
            GetSlotFunctionText(activeSlot.Key).Text = CompactLabel(activeSubmodeName);
        }
    }

    private static string CompactLabel(string text) => text.ToUpperInvariant() switch
    {
        "PER-APP MIXER" => "APP MIX",
        "AUDIO LOOPER" or "FOUR-TRACK" => "LOOPER",
        "AVATAR ACTIONS" => "AVATAR",
        "PRESENTATION" => "PRESENT",
        var value when value.Length > 9 => value[..9],
        var value => value,
    };

    private TextBlock GetSlotFunctionText(byte note) => note switch
    {
        0x20 => DjFunctionText,
        0x22 => ProductivityFunctionText,
        0x24 => MouseFunctionText,
        0x26 => MediaFunctionText,
        0x28 => VrChatFunctionText,
        _ => CustomFunctionText,
    };

    private ToggleButton GetSlotButton(byte note) => note switch
    {
        0x20 => DjModeButton,
        0x22 => ProductivityModeButton,
        0x24 => MouseModeButton,
        0x26 => MediaModeButton,
        0x28 => VrChatModeButton,
        _ => CustomModeButton,
    };

    private void UpdateVisualBank()
    {
        var bankBrush = CreateLedBrush(GetBankVelocity());
        var bankDimBrush = CreateLedBrush(GetBankVelocity(), 50);
        var ringDimBrush = CreateLedBrush(GetBankVelocity(), 110);
        var activeBrush = CreateLedBrush(GetActiveVelocity());
        var activeDimBrush = CreateLedBrush(GetActiveVelocity(), 78);
        var transportIdleBrush = CreateLedBrush(0x02);
        var transportIdleDimBrush = CreateLedBrush(0x02, 50);
        var transportActiveBrush = CreateLedBrush(0x01);
        var transportActiveDimBrush = CreateLedBrush(0x01, 78);
        var meterBrush = (SolidColorBrush)Application.Current.Resources["StantonRedBrush"];
        var meterDimBrush = new SolidColorBrush(ColorHelper.FromArgb(42, meterBrush.Color.R, meterBrush.Color.G, meterBrush.Color.B));

        foreach (var button in GetModeButtons())
        {
            var selected = string.Equals(button.Tag as string, activeMode, StringComparison.OrdinalIgnoreCase);
            button.BorderBrush = selected ? activeBrush : bankBrush;
            button.Background = selected ? activeDimBrush : bankDimBrush;
            button.Foreground = selected ? activeBrush : bankBrush;
        }

        foreach (var softButton in new[] { SoftButton1, SoftButton2, SoftButton3, SoftButton4 })
        {
            softButton.BorderBrush = bankBrush;
            softButton.Background = bankDimBrush;
        }
        if (IsMicrophoneMuteBank() && mediaOutputService.IsMicrophoneMuted)
        {
            SoftButton3.BorderBrush = activeBrush;
            SoftButton3.Background = activeDimBrush;
        }
        if (vrChatOscService is not null && vrChatOscService.MenuPointerActive && activeMode == "VRChat")
        {
            SoftButton4.BorderBrush = activeBrush;
            SoftButton4.Background = activeDimBrush;
        }
        if (audioLooperService is not null && activeMode == "Looper")
        {
            if (audioLooperService.IsRecording || audioLooperService.IsDubMode || audioLooperService.IsPunchHeld)
            {
                SoftButton1.BorderBrush = activeBrush;
                SoftButton1.Background = activeDimBrush;
            }
            if (tempoPulseOn || audioLooperService.IsShiftHeld || audioLooperService.IsDubMode)
            {
                SoftButton3.BorderBrush = activeBrush;
                SoftButton3.Background = activeDimBrush;
            }
            if (audioLooperService.LiveEffectName != "OFF")
            {
                SoftButton4.BorderBrush = activeBrush;
                SoftButton4.Background = activeDimBrush;
            }
        }
        if (activeMode == "Kaoss")
        {
            if (kaossService.Hold) { SoftButton1.BorderBrush = activeBrush; SoftButton1.Background = activeDimBrush; }
            if (kaossService.GateArp) { SoftButton2.BorderBrush = activeBrush; SoftButton2.Background = activeDimBrush; }
        }
        if (activeMode == "Mouse" && mouseTrackpadEngine is not null && mouseTrackpadEngine.IsMacroRecording)
        {
            SoftButton3.BorderBrush = activeBrush; SoftButton3.Background = activeDimBrush;
        }

        var transports = new[] { PlayButton, CueButton, SyncButton, TapButton };
        for (var index = 0; index < transports.Length; index++)
        {
            var transport = transports[index];
            var activeLooperTrack = false;
            if (audioLooperService is not null && activeMode == "Looper")
            {
                activeLooperTrack = audioLooperService.EraseArmedTrack == index ||
                                    (audioLooperService.IsRecording && audioLooperService.RecordingTrack == index)
                    ? looperFlashOn
                    : audioLooperService.HasTrackAudio(index);
            }
            if (activeMode == "Simon" && simonState.Pad == index && simonState.Lit) activeLooperTrack = true;
            if (activeMode == "Mouse" && index == 0 && mouseTrackpadEngine is not null && mouseTrackpadEngine.IsMacroPlaying) activeLooperTrack = true;
            transport.BorderBrush = activeLooperTrack ? transportActiveBrush : transportIdleBrush;
            transport.Background = activeLooperTrack ? transportActiveDimBrush : transportIdleDimBrush;
            if (transport.Child is TextBlock label)
            {
                label.Foreground = activeLooperTrack ? transportActiveBrush : transportIdleBrush;
            }
        }

        GainHeaderDot.Fill = bankBrush;
        PitchHeaderDot.Fill = bankBrush;
        SurfaceRing.Stroke = bankBrush;
        ActiveModeAccent.Background = activeBrush;
        SetMeterLevel(GainMeterLeds, gainLevel, meterBrush, meterDimBrush);
        SetMeterLevel(PitchMeterLeds, pitchLevel, meterBrush, meterDimBrush);
        SetMeterLevel(CenterMeterLeds, centerLevel, meterBrush, meterDimBrush);
        if (audioLooperService is not null && activeMode == "Looper")
        {
            SetFxRouteLeds(LeftFxTrackLeds, audioLooperService.IsTrackFxEnabled(0), audioLooperService.IsTrackFxEnabled(1), activeBrush, ringDimBrush);
            SetFxRouteLeds(RightFxTrackLeds, audioLooperService.IsTrackFxEnabled(2), audioLooperService.IsTrackFxEnabled(3), activeBrush, ringDimBrush);
        }
        else
        {
            SetFxRouteLeds(LeftFxTrackLeds, true, true, meterBrush, meterDimBrush);
            SetFxRouteLeds(RightFxTrackLeds, true, true, meterBrush, meterDimBrush);
        }
        for (var index = 0; index < ringLeds.Count; index++)
        {
            ringLeds[index].Fill = index == ringPosition ? activeBrush : ringDimBrush;
        }
    }

    private static void SetFxRouteLeds(StackPanel meter, bool upperEnabled, bool lowerEnabled, SolidColorBrush lit, SolidColorBrush dim)
    {
        for (var index = 0; index < meter.Children.Count; index++)
        {
            if (meter.Children[index] is Ellipse led) led.Fill = index < meter.Children.Count / 2
                ? upperEnabled ? lit : dim
                : lowerEnabled ? lit : dim;
        }
    }

    private static void SetMeterLevel(StackPanel meter, int level, SolidColorBrush lit, SolidColorBrush dim)
    {
        var litFrom = Math.Max(0, meter.Children.Count - level);
        for (var index = 0; index < meter.Children.Count; index++)
        {
            if (meter.Children[index] is Ellipse led)
            {
                led.Fill = index >= litFrom ? lit : dim;
            }
        }
    }

    private void BuildRingLeds()
    {
        const int count = 16;
        const double radius = 168;
        const double center = 175;
        const double size = 7;
        for (var index = 0; index < count; index++)
        {
            var angle = (index / (double)count * Math.Tau) - (Math.PI / 2d);
            var led = new Ellipse { Width = size, Height = size };
            Canvas.SetLeft(led, center + (Math.Cos(angle) * radius) - (size / 2d));
            Canvas.SetTop(led, center + (Math.Sin(angle) * radius) - (size / 2d));
            RingLedCanvas.Children.Add(led);
            ringLeds.Add(led);
        }
    }

    private void UpdateLiveSurfaceFeedback(MidiActivity activity)
    {
        if (activity.Kind == "Control" && activity.Data1 is 0x01 or 0x02 or 0x03 or 0x07 or 0x62)
        {
            UpdateVisualBank();
        }
    }

    private ToggleButton? GetModeButton(string modeName) => GetModeButtons().FirstOrDefault(button =>
        string.Equals(button.Tag as string, modeName, StringComparison.OrdinalIgnoreCase));

    private FrameworkElement? GetHardwareElement(MidiActivity activity)
    {
        if (activity.Kind is "Note on" or "Note off")
        {
            return activity.Data1 switch
            {
                0x07 => GainStrip,
                0x03 => PitchStrip,
                0x62 => SurfaceRing,
                0x01 => CenterStrip,
                0x2C => SoftButton1,
                0x2E => SoftButton2,
                0x30 => SoftButton3,
                0x32 => SoftButton4,
                0x6D => PlayButton,
                0x6E => CueButton,
                0x6F => SyncButton,
                0x70 => TapButton,
                _ => null,
            };
        }

        if (activity.Kind == "Control")
        {
            return activity.Data1 switch
            {
                0x62 or 0x63 => SurfaceRing,
                0x01 or 0x02 => CenterStrip,
                0x07 or 0x08 => GainStrip,
                0x03 or 0x04 => PitchStrip,
                _ => CenterStrip,
            };
        }

        return null;
    }

    private void Highlight(FrameworkElement? element)
    {
        RestoreHighlight();
        if (element is null)
        {
            return;
        }

        highlightedElement = element;
        highlightedOpacity = element.Opacity;
        if (element is Border border)
        {
            highlightedBackground = border.Background;
            highlightedBorderBrush = border.BorderBrush;
            var transport = border == PlayButton || border == CueButton || border == SyncButton || border == TapButton;
            border.Background = CreateLedBrush(transport ? (byte)0x01 : GetActiveVelocity(), 105);
            border.BorderBrush = CreateLedBrush(transport ? (byte)0x01 : GetActiveVelocity());
        }
        else if (element is ToggleButton toggleButton)
        {
            highlightedBackground = toggleButton.Background;
            highlightedBorderBrush = toggleButton.BorderBrush;
            toggleButton.Background = CreateLedBrush(GetActiveVelocity(), 105);
            toggleButton.BorderBrush = CreateLedBrush(GetActiveVelocity());
        }
        else
        {
            element.Opacity = 0.62;
        }
        highlightTimer.Stop();
        highlightTimer.Start();
    }

    private void OnHighlightTimerTick(object? sender, object e)
    {
        highlightTimer.Stop();
        RestoreHighlight();
    }

    private void RestoreHighlight()
    {
        if (highlightedElement is not null)
        {
            highlightedElement.Opacity = highlightedOpacity;
            highlightedElement = null;
            highlightedBackground = null;
            highlightedBorderBrush = null;
            UpdateVisualBank();
        }
    }

    private void AddActivity(string activity)
    {
        ActivityItems.Insert(0, activity);
        while (ActivityItems.Count > 40)
        {
            ActivityItems.RemoveAt(ActivityItems.Count - 1);
        }
    }

    private void OnClearActivityClicked(object sender, RoutedEventArgs e) => ActivityItems.Clear();

    private void OnPanicClicked(object sender, RoutedEventArgs e)
    {
        routingEnabled = false;
        customMappingService.ReleaseAll();
        mouseTrackpadEngine.ReleaseAll();
        djMidiService.ReleaseAll();
        productivityOutputService.ReleaseAll();
        audioLooperService.SetActive(false);
        kaossService.SetActive(false);
        pinkTromboneService.SetActive(false);
        shortcutService.ReleaseAll();
        simonService.Stop();
        vrChatOscService.ReleaseAll();
        UpdateRoutingIndicator();
        UpdateHardwareModeLeds();
        PreviewInfoBar.Severity = InfoBarSeverity.Warning;
        PreviewInfoBar.Title = "Routing paused";
        PreviewInfoBar.Message = "Audio and controller output stopped. Resume routing when ready.";
        StatusText.Text = "Safe stop active · observation continues";
        AddActivity($"{DateTime.Now:HH:mm:ss.fff}  SAFETY   Release all outputs");
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e) => Shutdown();

    public void Shutdown()
    {
        if (closed) return;
        closed = true;
        tempoLedTimer.Stop();
        audioDeviceTimer.Stop();
        simonService.Stop();
        productivityOutputService.ReleaseAll();
        shortcutService.ReleaseAll();
        customMappingService.ReleaseAll();
        midiService.Dispose();
        djMidiService.Dispose();
        mediaOutputService.Dispose();
        audioLooperService.Dispose();
        kaossService.Dispose();
        pinkTromboneService.Dispose();
        vrChatOscService.Dispose();
        captureService.Dispose();
        mouseTrackpadEngine.Dispose();
        highlightTimer.Stop();
        looperLedTimer.Stop();
    }
}
