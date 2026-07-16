namespace SCSCompanion.Services;

public sealed class AppSettingsService
{
    private readonly string themePath;
    private readonly string sensitivityPath;
    private readonly string moduleAssignmentsPath;
    private readonly string audioPreferencesPath;
    private readonly string simonHighScorePath;

    public AppSettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SCSCompanion");
        Directory.CreateDirectory(directory);
        themePath = Path.Combine(directory, "theme.txt");
        sensitivityPath = Path.Combine(directory, "mouse-sensitivity.txt");
        moduleAssignmentsPath = Path.Combine(directory, "module-assignments.json");
        audioPreferencesPath = Path.Combine(directory, "audio-preferences.json");
        simonHighScorePath = Path.Combine(directory, "simon-high-score.txt");
    }

    public string LoadTheme()
    {
        try
        {
            return File.Exists(themePath) ? File.ReadAllText(themePath).Trim() : "Stanton";
        }
        catch
        {
            return "Stanton";
        }
    }

    public void SaveTheme(string theme)
    {
        try
        {
            File.WriteAllText(themePath, theme);
        }
        catch
        {
            // The active theme still works if persistence is unavailable.
        }
    }

    public double LoadMouseSensitivity()
    {
        try
        {
            if (File.Exists(sensitivityPath) &&
                double.TryParse(File.ReadAllText(sensitivityPath), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return Math.Clamp(value, 0.5, 2.0);
            }
        }
        catch
        {
            // Use the default when persistence is unavailable.
        }

        return 1.0;
    }

    public void SaveMouseSensitivity(double sensitivity)
    {
        try
        {
            File.WriteAllText(sensitivityPath,
                Math.Clamp(sensitivity, 0.5, 2.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch
        {
            // The live setting still works if persistence is unavailable.
        }
    }

    public Dictionary<byte, string> LoadModuleAssignments(IReadOnlyDictionary<byte, string> defaults, ISet<string> availableModules)
    {
        try
        {
            if (File.Exists(moduleAssignmentsPath))
            {
                var saved = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(moduleAssignmentsPath));
                if (saved is not null)
                {
                    var result = new Dictionary<byte, string>(defaults);
                    foreach (var (noteText, module) in saved)
                    {
                        if (byte.TryParse(noteText, System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out var note) &&
                            result.ContainsKey(note) && availableModules.Contains(module))
                        {
                            result[note] = module;
                        }
                    }
                    return result;
                }
            }
        }
        catch
        {
            // Invalid or inaccessible configuration falls back to the known-good layout.
        }

        return new Dictionary<byte, string>(defaults);
    }

    public void SaveModuleAssignments(IReadOnlyDictionary<byte, string> assignments)
    {
        try
        {
            var serialized = assignments.ToDictionary(
                item => item.Key.ToString("X2", System.Globalization.CultureInfo.InvariantCulture),
                item => item.Value);
            File.WriteAllText(moduleAssignmentsPath,
                System.Text.Json.JsonSerializer.Serialize(serialized, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // The live assignment still works if persistence is unavailable.
        }
    }

    public AudioPreferences LoadAudioPreferences()
    {
        try
        {
            if (File.Exists(audioPreferencesPath))
            {
                var saved = System.Text.Json.JsonSerializer.Deserialize<AudioPreferences>(File.ReadAllText(audioPreferencesPath));
                if (saved is not null) return saved with { SyncCompensationMilliseconds = Math.Clamp(saved.SyncCompensationMilliseconds, -250, 250) };
            }
        }
        catch
        {
            // Invalid device identifiers fall back to the Windows defaults.
        }
        return new AudioPreferences(null, null, 0);
    }

    public void SaveAudioPreferences(AudioPreferences preferences)
    {
        try
        {
            var normalized = preferences with { SyncCompensationMilliseconds = Math.Clamp(preferences.SyncCompensationMilliseconds, -250, 250) };
            File.WriteAllText(audioPreferencesPath, System.Text.Json.JsonSerializer.Serialize(normalized,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // The current audio routing remains active if persistence is unavailable.
        }
    }

    public int LoadSimonHighScore()
    {
        try { return File.Exists(simonHighScorePath) && int.TryParse(File.ReadAllText(simonHighScorePath), out var score) ? Math.Max(0, score) : 0; }
        catch { return 0; }
    }

    public void SaveSimonHighScore(int score)
    {
        try { File.WriteAllText(simonHighScorePath, Math.Max(0, score).ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        catch { }
    }
}

public sealed record AudioPreferences(string? InputDeviceId, string? OutputDeviceId, int SyncCompensationMilliseconds);
