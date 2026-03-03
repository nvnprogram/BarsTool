using System.Text;

namespace BarsTool;

public class BarsListFile
{
    public string Name { get; set; } = string.Empty;
    public ushort Version { get; set; } = 1;
    public List<string> Entries { get; set; } = [];

    public static BarsListFile Read(string path) => Read(File.ReadAllBytes(path));

    public static BarsListFile Read(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms, Encoding.UTF8);

        uint magic = reader.ReadUInt32();
        if (magic != 0x4C535241) // "ARSL"
            throw new InvalidDataException("Not a valid BARSLIST file (expected ARSL magic).");

        ushort bom = reader.ReadUInt16();
        if (bom != 0xFEFF)
            throw new InvalidDataException($"Unexpected BOM: 0x{bom:X4}");

        var file = new BarsListFile();
        file.Version = reader.ReadUInt16();

        uint nameOffset = reader.ReadUInt32();
        int entryCount = reader.ReadInt32();

        var entryOffsets = new uint[entryCount];
        for (int i = 0; i < entryCount; i++)
            entryOffsets[i] = reader.ReadUInt32();

        long stringTableStart = reader.BaseStream.Position;

        file.Name = ReadNullTerminated(reader, stringTableStart + nameOffset);

        for (int i = 0; i < entryCount; i++)
            file.Entries.Add(ReadNullTerminated(reader, stringTableStart + entryOffsets[i]));

        return file;
    }

    public byte[] Write()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8);

        writer.Write(0x4C535241u); // "ARSL"
        writer.Write((ushort)0xFEFF);
        writer.Write(Version);

        var stringTable = new MemoryStream();
        var offsets = new List<uint>();

        uint nameOffset = (uint)stringTable.Position;
        WriteNullTerminated(stringTable, Name);

        foreach (string entry in Entries)
        {
            offsets.Add((uint)stringTable.Position);
            WriteNullTerminated(stringTable, entry);
        }

        writer.Write(nameOffset);
        writer.Write(Entries.Count);

        foreach (uint offset in offsets)
            writer.Write(offset);

        writer.Write(stringTable.ToArray());

        return ms.ToArray();
    }

    public void Save(string path) => File.WriteAllBytes(path, Write());

    public void AddEntry(string name)
    {
        if (!Entries.Contains(name))
            Entries.Add(name);
    }

    public bool RemoveEntry(string name) => Entries.Remove(name);

    private static string ReadNullTerminated(BinaryReader reader, long position)
    {
        long saved = reader.BaseStream.Position;
        reader.BaseStream.Position = position;

        var sb = new StringBuilder();
        byte b;
        while ((b = reader.ReadByte()) != 0)
            sb.Append((char)b);

        reader.BaseStream.Position = saved;
        return sb.ToString();
    }

    private static void WriteNullTerminated(Stream stream, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes);
        stream.WriteByte(0);
    }
}
