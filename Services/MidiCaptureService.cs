using System.Text;
using SCSCompanion.Models;

namespace SCSCompanion.Services;

public sealed class MidiCaptureService : IDisposable
{
    private readonly StreamWriter writer;

    public MidiCaptureService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SCSCompanion",
            "Captures");
        Directory.CreateDirectory(directory);
        CapturePath = Path.Combine(directory, $"scs3d_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        writer = new StreamWriter(CapturePath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
        writer.WriteLine("timestamp,kind,channel,data1,data2,raw_hex,description");
    }

    public string CapturePath { get; }

    public void Record(MidiActivity activity)
    {
        writer.WriteLine(string.Join(',',
            activity.Timestamp.ToString("O"),
            Csv(activity.Kind),
            activity.Channel,
            activity.Data1,
            activity.Data2,
            Csv(activity.RawHex),
            Csv(activity.Description)));
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    public void Dispose() => writer.Dispose();
}
