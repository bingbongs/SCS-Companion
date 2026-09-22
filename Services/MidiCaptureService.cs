using System.Text;
using System.Threading.Channels;
using SCSCompanion.Models;

namespace SCSCompanion.Services;

public sealed class MidiCaptureService : IDisposable
{
    private readonly Channel<MidiActivity> pending = Channel.CreateBounded<MidiActivity>(new BoundedChannelOptions(8192)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait,
    });
    private readonly Task writerTask;
    private int droppedMessages;
    public int DroppedMessages => Volatile.Read(ref droppedMessages);
    public string? LastError { get; private set; }

    public MidiCaptureService()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCSCompanion", "Captures");
        CapturePath = Path.Combine(directory, $"scs3d_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.csv");
        writerTask = Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                using var writer = new StreamWriter(CapturePath, false, new UTF8Encoding(false), 65536);
                await writer.WriteLineAsync("timestamp,kind,channel,data1,data2,raw_hex,description");
                var lastFlush = Environment.TickCount64;
                await foreach (var activity in pending.Reader.ReadAllAsync())
                {
                    await writer.WriteLineAsync(string.Join(',', activity.Timestamp.ToString("O"), Csv(activity.Kind),
                        activity.Channel, activity.Data1, activity.Data2, Csv(activity.RawHex), Csv(activity.Description)));
                    if (Environment.TickCount64 - lastFlush >= 1000)
                    {
                        await writer.FlushAsync();
                        lastFlush = Environment.TickCount64;
                    }
                }
            }
            catch (Exception exception) { LastError = exception.Message; pending.Writer.TryComplete(); }
        });
    }

    public string CapturePath { get; }
    public void Record(MidiActivity activity)
    {
        // Diagnostic I/O never blocks MIDI routing or the UI thread.
        if (!pending.Writer.TryWrite(activity)) Interlocked.Increment(ref droppedMessages);
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    public void Dispose() { pending.Writer.TryComplete(); writerTask.GetAwaiter().GetResult(); }
}
