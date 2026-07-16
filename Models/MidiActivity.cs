namespace SCSCompanion.Models;

public sealed record MidiActivity(
    DateTimeOffset Timestamp,
    string Kind,
    int Channel,
    int Data1,
    int Data2,
    byte[] Raw)
{
    public string RawHex => string.Join(" ", Raw.Select(value => value.ToString("X2")));

    public string Description => Kind switch
    {
        "Note on" => $"Note {Data1} · velocity {Data2}",
        "Note off" => $"Note {Data1} · released",
        "Control" => $"CC {Data1} · value {Data2}",
        "Pitch bend" => $"Pitch bend · {((Data2 << 7) | Data1) - 8192}",
        _ => RawHex,
    };

    public static MidiActivity FromRaw(byte[] raw)
    {
        if (raw.Length == 0)
        {
            return new(DateTimeOffset.Now, "Empty", 0, 0, 0, raw);
        }

        var status = raw[0];
        var messageType = status & 0xF0;
        var channel = (status & 0x0F) + 1;
        var data1 = raw.Length > 1 ? raw[1] : 0;
        var data2 = raw.Length > 2 ? raw[2] : 0;
        var kind = messageType switch
        {
            0x80 => "Note off",
            0x90 when data2 == 0 => "Note off",
            0x90 => "Note on",
            0xB0 => "Control",
            0xE0 => "Pitch bend",
            0xF0 => "System",
            _ => $"MIDI 0x{messageType:X2}",
        };

        return new(DateTimeOffset.Now, kind, channel, data1, data2, raw);
    }
}
