using System.Text;
using VGAudio.Containers;
using VGAudio.Containers.NintendoWare;
using VGAudio.Containers.Wave;

namespace BarsTool;

public record BfstmInfo(
    int SampleRate,
    int SampleCount,
    int ChannelCount,
    bool IsLooped,
    int LoopStart,
    byte Codec,
    int SampleBlockCount,
    int SampleBlockSize,
    int SampleBlockSampleCount,
    int LastBlockSize,
    int LastBlockSampleCount,
    int LastBlockPadSize,
    int SeekSize,
    int SeekIntervalSampleCount
);

public static class BfstmFile
{
    private const int FILE_HEADER_SIZE = 0x40;
    private const int PDAT_HEADER_SIZE = 0x40;
    private const int PREFETCH_BLOCKS_PER_CHANNEL = 5;

    public static BfstmInfo ReadInfo(byte[] data)
    {
        string magic = Encoding.ASCII.GetString(data, 0, 4);
        if (magic != "FSTM" && magic != "FSTP")
            throw new InvalidDataException($"Not a BFSTM/BFSTP file (got '{magic}').");

        int numBlocks = BitConverter.ToUInt16(data, 16);
        int infoOff = -1;
        int pos = 20;
        for (int i = 0; i < numBlocks; i++)
        {
            ushort blkType = BitConverter.ToUInt16(data, pos);
            int blkOff = BitConverter.ToInt32(data, pos + 4);
            if (blkType == 0x4000) infoOff = blkOff;
            pos += 12;
        }

        if (infoOff < 0) throw new InvalidDataException("INFO block not found.");

        int stmInfoRefOff = BitConverter.ToInt32(data, infoOff + 8 + 4);
        int si = infoOff + 8 + stmInfoRefOff;

        return new BfstmInfo(
            SampleRate: BitConverter.ToInt32(data, si + 4),
            SampleCount: BitConverter.ToInt32(data, si + 12),
            ChannelCount: data[si + 2],
            IsLooped: data[si + 1] != 0,
            LoopStart: BitConverter.ToInt32(data, si + 8),
            Codec: data[si],
            SampleBlockCount: BitConverter.ToInt32(data, si + 16),
            SampleBlockSize: BitConverter.ToInt32(data, si + 20),
            SampleBlockSampleCount: BitConverter.ToInt32(data, si + 24),
            LastBlockSize: BitConverter.ToInt32(data, si + 28),
            LastBlockSampleCount: BitConverter.ToInt32(data, si + 32),
            LastBlockPadSize: BitConverter.ToInt32(data, si + 36),
            SeekSize: BitConverter.ToInt32(data, si + 40),
            SeekIntervalSampleCount: BitConverter.ToInt32(data, si + 44)
        );
    }

    public static byte[] ConvertToWav(byte[] bfstmData)
    {
        var reader = new BCFstmReader();
        using var ms = new MemoryStream(bfstmData);
        var audioData = reader.Read(ms);
        var wavWriter = new WaveWriter();
        using var outStream = new MemoryStream();
        wavWriter.WriteToStream(audioData, outStream);
        return outStream.ToArray();
    }

    public static byte[] ConvertFromWav(byte[] wavData, bool loop = false, int loopStart = 0, int loopEnd = 0)
    {
        var wavReader = new WaveReader();
        using var ms = new MemoryStream(wavData);
        var audioData = wavReader.Read(ms);

        var writer = new BCFstmWriter(NwTarget.Cafe);
        writer.Configuration.Endianness = VGAudio.Utilities.Endianness.LittleEndian;
        writer.Configuration.Version = new NwVersion(5, 0, 0, 0);
        writer.Configuration.SamplesPerSeekTableEntry = 0x3800;
        writer.Configuration.SamplesPerInterleave = 0x3800;

        using var outStream = new MemoryStream();
        if (loop && loopEnd > 0)
        {
            var format = audioData.GetAllFormats().First();
            writer.WriteToStream(format.WithLoop(true, loopStart, loopEnd), outStream);
        }
        else
        {
            writer.WriteToStream(audioData, outStream);
        }
        return outStream.ToArray();
    }

    public static byte[] RoundtripBfstm(byte[] bfstmData)
    {
        var reader = new BCFstmReader();
        using var readMs = new MemoryStream(bfstmData);
        AudioWithConfig awc = reader.ReadWithConfig(readMs);

        var writer = new BCFstmWriter(NwTarget.Cafe);
        writer.Configuration.Endianness = VGAudio.Utilities.Endianness.LittleEndian;
        writer.Configuration.Version = new NwVersion(5, 0, 0, 0);
        writer.Configuration.SamplesPerSeekTableEntry = 0x3800;
        writer.Configuration.SamplesPerInterleave = 0x3800;
        writer.Configuration.RecalculateSeekTable = false;

        using var outMs = new MemoryStream();
        writer.WriteToStream(awc.AudioFormat, outMs, awc.Configuration);
        return outMs.ToArray();
    }

    public static byte[] GenerateBfstp(byte[] bfstmData)
    {
        string magic = Encoding.ASCII.GetString(bfstmData, 0, 4);
        if (magic != "FSTM")
            throw new InvalidDataException("Input must be a BFSTM file.");

        int numBlocks = BitConverter.ToUInt16(bfstmData, 16);

        int infoOff = -1, infoSize = -1;
        int dataOff = -1;
        int pos = 20;
        for (int i = 0; i < numBlocks; i++)
        {
            ushort blkType = BitConverter.ToUInt16(bfstmData, pos);
            int blkOff = BitConverter.ToInt32(bfstmData, pos + 4);
            int blkSize = BitConverter.ToInt32(bfstmData, pos + 8);
            if (blkType == 0x4000) { infoOff = blkOff; infoSize = blkSize; }
            if (blkType == 0x4002) { dataOff = blkOff; }
            pos += 12;
        }

        if (infoOff < 0 || dataOff < 0)
            throw new InvalidDataException("BFSTM missing INFO or DATA block.");

        byte[] infoBlock = new byte[infoSize];
        Array.Copy(bfstmData, infoOff, infoBlock, 0, infoSize);

        int stmInfoRefOff = BitConverter.ToInt32(infoBlock, 8 + 4);
        int stmInfoPos = 8 + stmInfoRefOff;
        int channels = infoBlock[stmInfoPos + 2];
        int sampleBlkSize = BitConverter.ToInt32(infoBlock, stmInfoPos + 20);

        int dataAudioStart = dataOff + 0x20;
        int prefetchDataSize = PREFETCH_BLOCKS_PER_CHANNEL * channels * sampleBlkSize;

        int pdatBlockSize = PDAT_HEADER_SIZE + prefetchDataSize;
        int pdatOff = FILE_HEADER_SIZE + infoSize;
        int fileSize = pdatOff + pdatBlockSize;

        int sampleDataRefFieldOff = stmInfoPos + 48 + 4;
        byte[] modifiedInfo = (byte[])infoBlock.Clone();
        int newSdRefValue = pdatOff + PDAT_HEADER_SIZE;
        BitConverter.TryWriteBytes(modifiedInfo.AsSpan(sampleDataRefFieldOff), newSdRefValue);

        using var ms = new MemoryStream(fileSize);
        using var w = new BinaryWriter(ms, Encoding.UTF8);

        w.Write(Encoding.ASCII.GetBytes("FSTP"));
        w.Write((ushort)0xFEFF); // BOM LE (bytes: FF FE)
        w.Write((ushort)FILE_HEADER_SIZE);
        w.Write(0x00040000u); // version
        w.Write(fileSize);
        w.Write((ushort)2); // 2 blocks
        w.Write((ushort)0);

        w.Write((ushort)0x4000); w.Write((ushort)0);
        w.Write(FILE_HEADER_SIZE); w.Write(infoSize);

        w.Write((ushort)0x4004); w.Write((ushort)0);
        w.Write(pdatOff); w.Write(pdatBlockSize);

        while (ms.Position < FILE_HEADER_SIZE) w.Write((byte)0);

        w.Write(modifiedInfo);

        // PDAT block
        w.Write(Encoding.ASCII.GetBytes("PDAT"));
        w.Write(pdatBlockSize);
        w.Write(1); // flag: 1 = Switch LE
        w.Write(0);
        w.Write(prefetchDataSize);
        w.Write(0);
        w.Write(0);
        w.Write(0x34); // data start offset
        while (ms.Position < pdatOff + PDAT_HEADER_SIZE) w.Write((byte)0);

        w.Write(bfstmData, dataAudioStart, prefetchDataSize);

        return ms.ToArray();
    }

    public static (float Loudness, float Peak) ComputeAudioMetrics(byte[] bfstmData)
    {
        byte[] wavData = ConvertToWav(bfstmData);

        using var ms = new MemoryStream(wavData);
        using var reader = new BinaryReader(ms, Encoding.UTF8);

        reader.ReadBytes(4); // RIFF
        reader.ReadInt32();
        reader.ReadBytes(4); // WAVE

        int channels = 0, bitsPerSample = 0;
        while (ms.Position < ms.Length)
        {
            string chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
            int chunkSize = reader.ReadInt32();
            if (chunkId == "fmt ")
            {
                reader.ReadInt16();
                channels = reader.ReadInt16();
                reader.ReadInt32(); reader.ReadInt32(); reader.ReadInt16();
                bitsPerSample = reader.ReadInt16();
                if (chunkSize > 16) reader.ReadBytes(chunkSize - 16);
            }
            else if (chunkId == "data")
            {
                if (bitsPerSample != 16 || chunkSize == 0) return (0f, 0f);
                int totalSamples = chunkSize / 2;
                double sumSquares = 0;
                int maxAbs = 0;
                for (int i = 0; i < totalSamples; i++)
                {
                    short sample = reader.ReadInt16();
                    int abs = Math.Abs((int)sample);
                    if (abs > maxAbs) maxAbs = abs;
                    sumSquares += sample / 32768.0 * (sample / 32768.0);
                }
                float peak = maxAbs / 32768f;
                double rms = Math.Sqrt(sumSquares / totalSamples);
                float loudness = rms > 0 ? (float)(20 * Math.Log10(rms)) : -100f;
                return (loudness, peak);
            }
            else reader.ReadBytes(chunkSize);
        }
        return (0f, 0f);
    }

    private static int AlignUp(int value, int alignment) =>
        (value + alignment - 1) / alignment * alignment;
}
