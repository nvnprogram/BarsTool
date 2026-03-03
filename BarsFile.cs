using System.Text;

namespace BarsTool;

public class BarsFile
{
    public ushort Version { get; set; } = 0x0101;
    public List<BarsAsset> Assets { get; set; } = [];

    public static BarsFile Read(string path) => Read(File.ReadAllBytes(path));

    public static BarsFile Read(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms, Encoding.UTF8);

        uint magic = reader.ReadUInt32();
        if (magic != 0x53524142) // "BARS"
            throw new InvalidDataException("Not a valid BARS file.");

        int fileSize = reader.ReadInt32();
        ushort bom = reader.ReadUInt16();
        if (bom != 0xFEFF)
            throw new InvalidDataException($"Unexpected BOM: 0x{bom:X4}");

        var bars = new BarsFile();
        bars.Version = reader.ReadUInt16();

        int assetCount = reader.ReadInt32();

        var hashes = new uint[assetCount];
        for (int i = 0; i < assetCount; i++)
            hashes[i] = reader.ReadUInt32();

        var amtaOffsets = new int[assetCount];
        var audioOffsets = new int[assetCount];
        for (int i = 0; i < assetCount; i++)
        {
            amtaOffsets[i] = reader.ReadInt32();
            audioOffsets[i] = reader.ReadInt32();
        }

        for (int i = 0; i < assetCount; i++)
        {
            var asset = new BarsAsset { Hash = hashes[i] };

            int amtaEnd = DetermineBlockEnd(amtaOffsets[i], i, amtaOffsets, audioOffsets, data.Length);
            int amtaLen = amtaEnd - amtaOffsets[i];
            asset.AmtaData = new byte[amtaLen];
            Array.Copy(data, amtaOffsets[i], asset.AmtaData, 0, amtaLen);

            try
            {
                var amta = AmtaFile.Read(asset.AmtaData);
                asset.Name = amta.Name;
            }
            catch
            {
                asset.Name = $"unknown_{hashes[i]:X8}";
            }

            // Read audio data (may be -1 / 0xFFFFFFFF if no audio)
            if (audioOffsets[i] != -1 && audioOffsets[i] != 0)
            {
                int audioEnd = DetermineAudioEnd(audioOffsets[i], i, audioOffsets, data.Length);
                int audioLen = audioEnd - audioOffsets[i];
                asset.AudioData = new byte[audioLen];
                Array.Copy(data, audioOffsets[i], asset.AudioData, 0, audioLen);
            }

            bars.Assets.Add(asset);
        }

        return bars;
    }

    public byte[] Write()
    {
        // Sort assets by CRC32 hash for binary search
        var sorted = Assets.OrderBy(a => a.Hash).ToList();

        int assetCount = sorted.Count;
        int headerSize = 0x10 + assetCount * 4 + assetCount * 8;

        var amtaPositions = new int[assetCount];
        int pos = headerSize;
        for (int i = 0; i < assetCount; i++)
        {
            amtaPositions[i] = pos;
            pos += sorted[i].AmtaData?.Length ?? 0;
        }

        var audioPositions = new int[assetCount];
        for (int i = 0; i < assetCount; i++)
        {
            if (sorted[i].AudioData == null || sorted[i].AudioData!.Length == 0)
            {
                audioPositions[i] = -1;
            }
            else
            {
                pos = AlignUp(pos, 64);
                audioPositions[i] = pos;
                pos += sorted[i].AudioData!.Length;
            }
        }

        int fileSize = pos;

        using var ms = new MemoryStream(fileSize);
        using var writer = new BinaryWriter(ms, Encoding.UTF8);

        writer.Write(Encoding.ASCII.GetBytes("BARS"));
        writer.Write(fileSize);
        writer.Write((ushort)0xFEFF);
        writer.Write(Version);
        writer.Write(assetCount);

        foreach (var asset in sorted)
            writer.Write(asset.Hash);

        for (int i = 0; i < assetCount; i++)
        {
            writer.Write(amtaPositions[i]);
            writer.Write(audioPositions[i]);
        }

        for (int i = 0; i < assetCount; i++)
        {
            if (sorted[i].AmtaData != null)
                writer.Write(sorted[i].AmtaData!);
        }

        for (int i = 0; i < assetCount; i++)
        {
            if (audioPositions[i] == -1)
                continue;

            // Pad to alignment
            while (ms.Position < audioPositions[i])
                writer.Write((byte)0);

            writer.Write(sorted[i].AudioData!);
        }

        return ms.ToArray();
    }

    public void Save(string path) => File.WriteAllBytes(path, Write());

    public void AddAudio(string name, byte[] amtaData, byte[]? audioData)
    {
        uint hash = Crc32.Compute(name);

        // Remove existing with same hash
        Assets.RemoveAll(a => a.Hash == hash);

        Assets.Add(new BarsAsset
        {
            Hash = hash,
            Name = name,
            AmtaData = amtaData,
            AudioData = audioData
        });
    }

    public bool RemoveAsset(string name)
    {
        uint hash = Crc32.Compute(name);
        int removed = Assets.RemoveAll(a => a.Hash == hash);
        return removed > 0;
    }

    public BarsAsset? FindAsset(string name)
    {
        uint hash = Crc32.Compute(name);
        return Assets.FirstOrDefault(a => a.Hash == hash);
    }

    private static int DetermineBlockEnd(int blockStart, int index, int[] amtaOffsets, int[] audioOffsets, int fileSize)
    {
        int minNext = fileSize;
        for (int i = 0; i < amtaOffsets.Length; i++)
        {
            if (amtaOffsets[i] > blockStart && amtaOffsets[i] < minNext)
                minNext = amtaOffsets[i];
            if (audioOffsets[i] > 0 && audioOffsets[i] != -1 && audioOffsets[i] > blockStart && audioOffsets[i] < minNext)
                minNext = audioOffsets[i];
        }
        return minNext;
    }

    private static int DetermineAudioEnd(int audioStart, int index, int[] audioOffsets, int fileSize)
    {
        int minNext = fileSize;
        for (int i = 0; i < audioOffsets.Length; i++)
        {
            if (audioOffsets[i] > 0 && audioOffsets[i] != -1 && audioOffsets[i] > audioStart && audioOffsets[i] < minNext)
                minNext = audioOffsets[i];
        }
        return minNext;
    }

    private static int AlignUp(int value, int alignment) =>
        (value + alignment - 1) / alignment * alignment;
}

public class BarsAsset
{
    public uint Hash { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte[]? AmtaData { get; set; }
    public byte[]? AudioData { get; set; }
}
