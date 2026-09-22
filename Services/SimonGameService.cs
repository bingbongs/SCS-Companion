using SCSCompanion.Models;

namespace SCSCompanion.Services;

public sealed class SimonGameService
{
    private static readonly byte[] Pads = [0x6D, 0x6E, 0x6F, 0x70];
    private readonly Random random = new();
    private readonly List<int> sequence = [];
    private int inputIndex;
    private bool accepting;
    private int generation;
    public int Score => Math.Max(0, sequence.Count - 1);
    public int HighScore { get; private set; }
    public bool IsPlaying => sequence.Count > 0;
    public event EventHandler<SimonState>? StateChanged;

    public SimonGameService(int highScore) => HighScore = Math.Max(0, highScore);

    public void Handle(MidiActivity activity, bool enabled)
    {
        if (!enabled || activity.Kind != "Note on") return;
        if (activity.Data1 == 0x2C && !IsPlaying) { Start(); return; }
        var pad = Array.IndexOf(Pads, (byte)activity.Data1);
        if (pad < 0 || !accepting) return;
        StateChanged?.Invoke(this, new("input", Score, HighScore, pad, false));
        if (pad != sequence[inputIndex])
        {
            accepting = false; HighScore = Math.Max(HighScore, Score);
            StateChanged?.Invoke(this, new("lost", Score, HighScore, pad, true));
            sequence.Clear(); return;
        }
        inputIndex++;
        if (inputIndex == sequence.Count) { accepting = false; _ = NextRoundAsync(generation); }
    }

    public void Start() { generation++; sequence.Clear(); inputIndex = 0; accepting = false; _ = NextRoundAsync(generation); }
    public void Stop() { generation++; sequence.Clear(); accepting = false; StateChanged?.Invoke(this, new("ready", 0, HighScore, -1, false)); }
    private async Task NextRoundAsync(int run)
    {
        await Task.Delay(sequence.Count == 0 ? 150 : 500);
        if (run != generation) return;
        sequence.Add(random.Next(4)); inputIndex = 0;
        StateChanged?.Invoke(this, new("watch", Score, HighScore, -1, false));
        foreach (var pad in sequence.ToArray())
        {
            if (run != generation) return;
            StateChanged?.Invoke(this, new("flash", Score, HighScore, pad, true)); await Task.Delay(Math.Max(170, 430 - Score * 8));
            if (run != generation) return;
            StateChanged?.Invoke(this, new("flash", Score, HighScore, pad, false)); await Task.Delay(110);
        }
        if (run != generation) return;
        accepting = true; StateChanged?.Invoke(this, new("repeat", Score, HighScore, -1, false));
    }
}

public sealed record SimonState(string Phase, int Score, int HighScore, int Pad, bool Lit);
