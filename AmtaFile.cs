using System.Text;

namespace BarsTool;

public class AmtaFile
{
    public string Name { get; set; } = string.Empty;
    public byte Version { get; set; } = 4;
    public int UnknownDataField { get; set; }
    public byte TrackType { get; set; }
    public byte WaveChannelCount { get; set; }
    public byte StreamTrackCount { get; set; }
    public byte Flags { get; set; }
    public float Duration { get; set; }
    public int SampleRate { get; set; }
    public int LoopStart { get; set; }
    public int LoopEnd { get; set; }
    public float Loudness { get; set; }
    public float AmplitudePeak { get; set; }
    public List<(int ChannelCount, float Volume)> StreamTracks { get; set; } = [];
    public List<AmtaMarker> Markers { get; set; } = [];
    public List<(int V1, int V2)> ExtEntries { get; set; } = [];

    public byte[]? RawBytes { get; set; }

    public static AmtaFile Read(byte[] data) => Read(data, 0, data.Length);

    public static AmtaFile Read(byte[] data, int offset, int length)
    {
        using var ms = new MemoryStream(data, offset, length);
        using var reader = new BinaryReader(ms, Encoding.UTF8);

        var amta = new AmtaFile();
        amta.RawBytes = new byte[length];
        Array.Copy(data, offset, amta.RawBytes, 0, length);

        long amtaStart = 0;

        uint magic = reader.ReadUInt32();
        if (magic != 0x41544D41) // "AMTA"
            throw new InvalidDataException("Not a valid AMTA block.");

        reader.ReadUInt16(); // BOM (0xFEFF)
        reader.ReadByte();   // padding at offset 6
        amta.Version = reader.ReadByte(); // version at offset 7

        int totalSize = reader.ReadInt32();   // 0x08: total AMTA size
        int dataOffset = reader.ReadInt32();  // 0x0C: DATA offset from AMTA start
        int markOffset = reader.ReadInt32();  // 0x10: MARK offset from AMTA start
        int extOffset = reader.ReadInt32();   // 0x14: EXT_ offset from AMTA start
        int strgOffset = reader.ReadInt32();  // 0x18: STRG offset from AMTA start

        // STRG section layout: "STRG"(4) + stringDataSize(4) + null-terminated strings
        long strgDataStart = amtaStart + strgOffset + 8;

        ms.Position = amtaStart + dataOffset;
        uint dataMagic = reader.ReadUInt32();
        if (dataMagic != 0x41544144) // "DATA"
            throw new InvalidDataException("Expected DATA section in AMTA.");
        reader.ReadInt32(); // content size

        uint nameOffsetInStrg = reader.ReadUInt32();
        amta.UnknownDataField = reader.ReadInt32();
        amta.TrackType = reader.ReadByte();
        amta.WaveChannelCount = reader.ReadByte();
        amta.StreamTrackCount = reader.ReadByte();
        amta.Flags = reader.ReadByte();
        amta.Duration = reader.ReadSingle();
        amta.SampleRate = reader.ReadInt32();
        amta.LoopStart = reader.ReadInt32();
        amta.LoopEnd = reader.ReadInt32();
        amta.Loudness = reader.ReadSingle();

        amta.StreamTracks = new List<(int, float)>();
        for (int i = 0; i < 8; i++)
            amta.StreamTracks.Add((reader.ReadInt32(), reader.ReadSingle()));

        if (amta.Version >= 4)
            amta.AmplitudePeak = reader.ReadSingle();

        long savedPos = ms.Position;
        ms.Position = strgDataStart + nameOffsetInStrg;
        amta.Name = ReadNullTerminated(reader);
        ms.Position = savedPos;

        ms.Position = amtaStart + markOffset;
        uint markMagic = reader.ReadUInt32();
        if (markMagic != 0x4B52414D) // "MARK"
            throw new InvalidDataException("Expected MARK section in AMTA.");
        reader.ReadInt32(); // content size
        int markCount = reader.ReadInt32();
        for (int i = 0; i < markCount; i++)
        {
            var marker = new AmtaMarker();
            marker.Id = reader.ReadInt32();
            uint markerNameOff = reader.ReadUInt32();

            savedPos = ms.Position;
            ms.Position = strgDataStart + markerNameOff;
            marker.Name = ReadNullTerminated(reader);
            ms.Position = savedPos;

            marker.StartPos = reader.ReadInt32();
            marker.Length = reader.ReadInt32();
            amta.Markers.Add(marker);
        }

        ms.Position = amtaStart + extOffset;
        uint extMagic = reader.ReadUInt32();
        if (extMagic != 0x5F545845) // "EXT_"
            throw new InvalidDataException("Expected EXT_ section in AMTA.");
        reader.ReadInt32(); // content size
        int extCount = reader.ReadInt32();
        for (int i = 0; i < extCount; i++)
            amta.ExtEntries.Add((reader.ReadInt32(), reader.ReadInt32()));

        return amta;
    }

    private static string ReadNullTerminated(BinaryReader reader)
    {
        var sb = new StringBuilder();
        byte b;
        while (reader.BaseStream.Position < reader.BaseStream.Length && (b = reader.ReadByte()) != 0)
            sb.Append((char)b);
        return sb.ToString();
    }

    public byte[] Write()
    {
        if (RawBytes != null)
            return RawBytes;

        return BuildNew();
    }

    public byte[] BuildNew()
    {
        var strgStream = new MemoryStream();

        uint nameStrgOffset = 0;
        byte[] nameBytes = Encoding.UTF8.GetBytes(Name);
        strgStream.Write(nameBytes);
        strgStream.WriteByte(0);

        var markerStrgOffsets = new List<uint>();
        foreach (var marker in Markers)
        {
            markerStrgOffsets.Add((uint)strgStream.Position);
            byte[] mNameBytes = Encoding.UTF8.GetBytes(marker.Name);
            strgStream.Write(mNameBytes);
            strgStream.WriteByte(0);
        }

        byte[] strgData = strgStream.ToArray();

        var dataContent = new MemoryStream();
        var dataWriter = new BinaryWriter(dataContent, Encoding.UTF8);
        dataWriter.Write(nameStrgOffset);
        dataWriter.Write(UnknownDataField);
        dataWriter.Write(TrackType);
        dataWriter.Write(WaveChannelCount);
        dataWriter.Write(StreamTrackCount);
        dataWriter.Write(Flags);
        dataWriter.Write(Duration);
        dataWriter.Write(SampleRate);
        dataWriter.Write(LoopStart);
        dataWriter.Write(LoopEnd);
        dataWriter.Write(Loudness);

        for (int i = 0; i < 8; i++)
        {
            if (i < StreamTracks.Count)
            {
                dataWriter.Write(StreamTracks[i].ChannelCount);
                dataWriter.Write(StreamTracks[i].Volume);
            }
            else
            {
                dataWriter.Write(0);
                dataWriter.Write(0f);
            }
        }

        if (Version >= 4)
            dataWriter.Write(AmplitudePeak);

        byte[] dataBytes = dataContent.ToArray();

        var markContent = new MemoryStream();
        var markWriter = new BinaryWriter(markContent, Encoding.UTF8);
        markWriter.Write(Markers.Count);
        for (int i = 0; i < Markers.Count; i++)
        {
            markWriter.Write(Markers[i].Id);
            markWriter.Write(i < markerStrgOffsets.Count ? markerStrgOffsets[i] : 0u);
            markWriter.Write(Markers[i].StartPos);
            markWriter.Write(Markers[i].Length);
        }
        byte[] markBytes = markContent.ToArray();

        var extContent = new MemoryStream();
        var extWriter = new BinaryWriter(extContent, Encoding.UTF8);
        extWriter.Write(ExtEntries.Count);
        foreach (var (v1, v2) in ExtEntries)
        {
            extWriter.Write(v1);
            extWriter.Write(v2);
        }
        byte[] extBytes = extContent.ToArray();

        int headerSize = 0x1C; // magic(4) + bom(2) + pad(1) + ver(1) + totalSize(4) + 4*offset(16)

        int dataSecStart = headerSize;
        int dataSecSize = 8 + dataBytes.Length;

        int markSecStart = dataSecStart + dataSecSize;
        int markSecSize = 8 + markBytes.Length;

        int extSecStart = markSecStart + markSecSize;
        int extSecSize = 8 + extBytes.Length;

        int strgSecStart = extSecStart + extSecSize;
        int strgSecSize = 8 + strgData.Length;

        int totalSize = strgSecStart + strgSecSize;

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8);

        writer.Write(0x41544D41u); // "AMTA"
        writer.Write((ushort)0xFEFF); // BOM
        writer.Write((byte)0); // padding at offset 6
        writer.Write(Version); // version at offset 7
        writer.Write(totalSize);
        writer.Write(dataSecStart);
        writer.Write(markSecStart);
        writer.Write(extSecStart);
        writer.Write(strgSecStart);

        writer.Write(0x41544144u); // "DATA"
        writer.Write(dataBytes.Length); // content size
        writer.Write(dataBytes);

        writer.Write(0x4B52414Du); // "MARK"
        writer.Write(markBytes.Length); // content size
        writer.Write(markBytes);

        writer.Write(0x5F545845u); // "EXT_"
        writer.Write(extBytes.Length); // content size
        writer.Write(extBytes);

        writer.Write(0x47525453u); // "STRG"
        writer.Write(strgData.Length); // string data size
        writer.Write(strgData);

        return ms.ToArray();
    }

    public static AmtaFile CreateFromBfwav(string name, BfwavInfo info)
    {
        return new AmtaFile
        {
            Name = name,
            Version = 4,
            UnknownDataField = info.SampleCount,
            TrackType = 0, // Wave
            WaveChannelCount = (byte)info.ChannelCount,
            StreamTrackCount = 0,
            Flags = 2,
            Duration = info.SampleCount / (float)info.SampleRate,
            SampleRate = info.SampleRate,
            LoopStart = info.IsLooped ? info.LoopStart : 0,
            LoopEnd = info.IsLooped ? info.SampleCount : info.SampleCount,
            Loudness = 0f,
            AmplitudePeak = 0f,
            StreamTracks = [],
            Markers = [],
            ExtEntries = [],
            RawBytes = null
        };
    }
}

public class AmtaMarker
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int StartPos { get; set; }
    public int Length { get; set; }
}
