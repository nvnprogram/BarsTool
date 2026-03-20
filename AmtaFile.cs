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
    public List<AmtaExtEntry> ExtEntries { get; set; } = [];

    public static AmtaFile Read(byte[] data) => Read(data, 0, data.Length);

    public static AmtaFile Read(byte[] data, int offset, int length)
    {
        using var ms = new MemoryStream(data, offset, length);
        using var reader = new BinaryReader(ms, Encoding.UTF8);

        var amta = new AmtaFile();

        uint magic = reader.ReadUInt32();
        if (magic != 0x41544D41) // "AMTA"
            throw new InvalidDataException("Not a valid AMTA block.");

        reader.ReadUInt16(); // BOM (0xFEFF)
        reader.ReadByte();   // padding
        amta.Version = reader.ReadByte();

        int totalSize = reader.ReadInt32();   // 0x08
        int dataOffset = reader.ReadInt32();  // 0x0C
        int markOffset = reader.ReadInt32();  // 0x10
        int extOffset = reader.ReadInt32();   // 0x14
        int strgOffset = reader.ReadInt32();  // 0x18

        long strgDataStart = strgOffset + 8;

        ms.Position = dataOffset;
        reader.ReadUInt32(); // "DATA"
        reader.ReadInt32();  // content size

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

        ms.Position = strgDataStart + nameOffsetInStrg;
        amta.Name = Encoding.UTF8.GetString(ReadRawNullTerminated(reader));

        ms.Position = markOffset;
        reader.ReadUInt32(); // "MARK"
        reader.ReadInt32();  // content size
        int markCount = reader.ReadInt32();
        for (int i = 0; i < markCount; i++)
        {
            var marker = new AmtaMarker();
            marker.Id = reader.ReadInt32();
            uint markerNameOff = reader.ReadUInt32();

            long savedPos = ms.Position;
            ms.Position = strgDataStart + markerNameOff;
            marker.NameBytes = ReadRawNullTerminated(reader);
            ms.Position = savedPos;

            marker.StartPos = reader.ReadInt32();
            marker.Length = reader.ReadInt32();
            amta.Markers.Add(marker);
        }

        ms.Position = extOffset;
        reader.ReadUInt32(); // "EXT_"
        reader.ReadInt32();  // content size
        int extCount = reader.ReadInt32();
        for (int i = 0; i < extCount; i++)
        {
            int nameOff = reader.ReadInt32();
            int rawValue = reader.ReadInt32();

            long savedPos = ms.Position;
            ms.Position = strgDataStart + nameOff;
            byte[] nameBytes = ReadRawNullTerminated(reader);
            ms.Position = savedPos;

            amta.ExtEntries.Add(new AmtaExtEntry { NameBytes = nameBytes, RawValue = rawValue });
        }

        return amta;
    }

    private static byte[] ReadRawNullTerminated(BinaryReader reader)
    {
        var bytes = new List<byte>();
        byte b;
        while (reader.BaseStream.Position < reader.BaseStream.Length && (b = reader.ReadByte()) != 0)
            bytes.Add(b);
        return bytes.ToArray();
    }

    public byte[] Write() => BuildNew();

    public byte[] BuildNew()
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(Name);

        // Build STRG with exact-match string deduplication.
        // Process in order: asset name, then markers, then ext entries.
        var strgBuf = new List<byte>();
        var knownOffsets = new Dictionary<ByteArrayKey, int>();

        int WriteOrReuse(byte[] bytes)
        {
            var key = new ByteArrayKey(bytes);
            if (knownOffsets.TryGetValue(key, out int existing))
                return existing;
            int off = strgBuf.Count;
            knownOffsets[key] = off;
            strgBuf.AddRange(bytes);
            strgBuf.Add(0);
            return off;
        }

        WriteOrReuse(nameBytes);

        var markerStrgOffsets = new uint[Markers.Count];
        for (int i = 0; i < Markers.Count; i++)
            markerStrgOffsets[i] = (uint)WriteOrReuse(Markers[i].NameBytes);

        var extStrgOffsets = new uint[ExtEntries.Count];
        for (int i = 0; i < ExtEntries.Count; i++)
            extStrgOffsets[i] = (uint)WriteOrReuse(ExtEntries[i].NameBytes);

        byte[] strgData = strgBuf.ToArray();

        // DATA section content
        var dataContent = new MemoryStream();
        var dw = new BinaryWriter(dataContent, Encoding.UTF8);
        dw.Write((uint)0); // name offset always 0
        dw.Write(UnknownDataField);
        dw.Write(TrackType);
        dw.Write(WaveChannelCount);
        dw.Write(StreamTrackCount);
        dw.Write(Flags);
        dw.Write(Duration);
        dw.Write(SampleRate);
        dw.Write(LoopStart);
        dw.Write(LoopEnd);
        dw.Write(Loudness);
        for (int i = 0; i < 8; i++)
        {
            if (i < StreamTracks.Count)
            {
                dw.Write(StreamTracks[i].ChannelCount);
                dw.Write(StreamTracks[i].Volume);
            }
            else
            {
                dw.Write(0);
                dw.Write(0f);
            }
        }
        if (Version >= 4)
            dw.Write(AmplitudePeak);
        byte[] dataBytes = dataContent.ToArray();

        // MARK section content
        var markContent = new MemoryStream();
        var mw = new BinaryWriter(markContent, Encoding.UTF8);
        mw.Write(Markers.Count);
        for (int i = 0; i < Markers.Count; i++)
        {
            mw.Write(Markers[i].Id);
            mw.Write(markerStrgOffsets[i]);
            mw.Write(Markers[i].StartPos);
            mw.Write(Markers[i].Length);
        }
        byte[] markBytes = markContent.ToArray();

        // EXT_ section content
        var extContent = new MemoryStream();
        var ew = new BinaryWriter(extContent, Encoding.UTF8);
        ew.Write(ExtEntries.Count);
        for (int i = 0; i < ExtEntries.Count; i++)
        {
            ew.Write((int)extStrgOffsets[i]);
            ew.Write(ExtEntries[i].RawValue);
        }
        byte[] extBytes = extContent.ToArray();

        // Compute section offsets
        int headerSize = 0x1C;
        int dataSecStart = headerSize;
        int markSecStart = dataSecStart + 8 + dataBytes.Length;
        int extSecStart = markSecStart + 8 + markBytes.Length;
        int strgSecStart = extSecStart + 8 + extBytes.Length;
        int totalSize = AlignUp(strgSecStart + 8 + strgData.Length, 4);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8);

        writer.Write(0x41544D41u); // "AMTA"
        writer.Write((ushort)0xFEFF);
        writer.Write((byte)0);
        writer.Write(Version);
        writer.Write(totalSize);
        writer.Write(dataSecStart);
        writer.Write(markSecStart);
        writer.Write(extSecStart);
        writer.Write(strgSecStart);

        writer.Write(0x41544144u); // "DATA"
        writer.Write(dataBytes.Length);
        writer.Write(dataBytes);

        writer.Write(0x4B52414Du); // "MARK"
        writer.Write(markBytes.Length);
        writer.Write(markBytes);

        writer.Write(0x5F545845u); // "EXT_"
        writer.Write(extBytes.Length);
        writer.Write(extBytes);

        writer.Write(0x47525453u); // "STRG"
        writer.Write(strgData.Length);
        writer.Write(strgData);

        while (ms.Position < totalSize)
            writer.Write((byte)0);

        return ms.ToArray();
    }

    private readonly struct ByteArrayKey : IEquatable<ByteArrayKey>
    {
        private readonly byte[] _data;
        private readonly int _hash;

        public ByteArrayKey(byte[] data)
        {
            _data = data;
            int h = 17;
            foreach (byte b in data) h = h * 31 + b;
            _hash = h;
        }

        public override int GetHashCode() => _hash;
        public override bool Equals(object? obj) => obj is ByteArrayKey other && Equals(other);
        public bool Equals(ByteArrayKey other) => _data.AsSpan().SequenceEqual(other._data);
    }

    public static AmtaFile CreateFromBfstm(string name, BfstmInfo info, byte[] bfstmData)
    {
        var (loudness, peak) = BfstmFile.ComputeAudioMetrics(bfstmData);

        byte flags = 2;
        if (info.IsLooped) flags |= 1;
        flags |= 4;

        int trackCount = 1;
        var streamTracks = new List<(int ChannelCount, float Volume)>();
        if (info.ChannelCount <= 2)
        {
            streamTracks.Add((info.ChannelCount, 1.0f));
        }
        else
        {
            trackCount = info.ChannelCount / 2;
            for (int i = 0; i < trackCount; i++)
                streamTracks.Add((2, 1.0f));
        }

        return new AmtaFile
        {
            Name = name,
            Version = 4,
            UnknownDataField = info.SampleCount,
            TrackType = 1,
            WaveChannelCount = (byte)info.ChannelCount,
            StreamTrackCount = (byte)trackCount,
            Flags = flags,
            Duration = info.SampleCount / (float)info.SampleRate,
            SampleRate = info.SampleRate,
            LoopStart = info.IsLooped ? info.LoopStart : 0,
            LoopEnd = info.SampleCount,
            Loudness = loudness,
            AmplitudePeak = peak,
            StreamTracks = streamTracks,
            Markers = [],
            ExtEntries = [new AmtaExtEntry { NameBytes = "ltl"u8.ToArray(), RawValue = BitConverter.SingleToInt32Bits(loudness) }],
        };
    }

    public static AmtaFile CreateFromBfwav(string name, BfwavInfo info, byte[] bfwavData)
    {
        var (loudness, peak) = BfwavFile.ComputeAudioMetrics(bfwavData);
        int normalizedSamples = (int)Math.Ceiling((long)info.SampleCount * 48000.0 / info.SampleRate);

        return new AmtaFile
        {
            Name = name,
            Version = 4,
            UnknownDataField = normalizedSamples,
            TrackType = 0,
            WaveChannelCount = (byte)info.ChannelCount,
            StreamTrackCount = 0,
            Flags = 2,
            Duration = info.SampleCount / (float)info.SampleRate,
            SampleRate = info.SampleRate,
            LoopStart = info.IsLooped ? info.LoopStart : 0,
            LoopEnd = info.SampleCount,
            Loudness = loudness,
            AmplitudePeak = peak,
            StreamTracks = [],
            Markers = [],
            ExtEntries = [new AmtaExtEntry { NameBytes = "ltl"u8.ToArray(), RawValue = BitConverter.SingleToInt32Bits(loudness) }],
        };
    }

    private static int AlignUp(int value, int alignment) =>
        (value + alignment - 1) / alignment * alignment;
}

public class AmtaMarker
{
    public int Id { get; set; }
    public byte[] NameBytes { get; set; } = [];
    public string Name => Encoding.UTF8.GetString(NameBytes);
    public int StartPos { get; set; }
    public int Length { get; set; }
}

public class AmtaExtEntry
{
    public byte[] NameBytes { get; set; } = [];
    public string Name => Encoding.UTF8.GetString(NameBytes);
    public int RawValue { get; set; }
}
