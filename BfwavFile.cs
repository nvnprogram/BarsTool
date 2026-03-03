using System.Text;
using VGAudio.Codecs.GcAdpcm;
using VGAudio.Containers.NintendoWare;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.GcAdpcm;

namespace BarsTool;

public record BfwavInfo(
    int SampleRate,
    int SampleCount,
    int ChannelCount,
    bool IsLooped,
    int LoopStart,
    byte Encoding
);

public static class BfwavFile
{
    private const ushort BOM_LE = 0xFEFF;
    private const int HEADER_SIZE = 0x40;
    private const uint VERSION_10200 = 0x00010200;
    private const int DATA_ALIGNMENT = 0x40; // 64 bytes for v0x10200

    private const ushort TYPE_INFO_BLOCK = 0x7000;
    private const ushort TYPE_DATA_BLOCK = 0x7001;
    private const ushort TYPE_WAVE_CHANNEL_INFO = 0x7100;
    private const ushort TYPE_SAMPLE_DATA = 0x1F00;
    private const ushort TYPE_GC_ADPCM_INFO = 0x0300;

    public static BfwavInfo ReadInfo(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms, Encoding.UTF8);

        string magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != "FWAV")
            throw new InvalidDataException("Not a BFWAV file.");

        ushort bom = reader.ReadUInt16();
        reader.ReadInt16(); // header size
        uint version = reader.ReadUInt32();
        reader.ReadInt32(); // file size
        reader.ReadInt16(); // block count
        reader.ReadInt16(); // padding

        reader.ReadInt16(); // type
        reader.ReadInt16(); // pad
        int infoOffset = reader.ReadInt32();
        reader.ReadInt32(); // info size

        // Seek to INFO block content (skip magic + size)
        ms.Position = infoOffset + 8;

        byte encoding = reader.ReadByte();
        bool isLooped = reader.ReadByte() != 0;
        reader.ReadInt16(); // padding
        int sampleRate = reader.ReadInt32();
        int loopStart = reader.ReadInt32();
        int sampleCount = reader.ReadInt32();
        reader.ReadInt32(); // adjusted loop start / reserved
        int channelCount = reader.ReadInt32();

        return new BfwavInfo(sampleRate, sampleCount, channelCount, isLooped, loopStart, encoding);
    }

    public static byte[] ConvertFromWav(byte[] wavData)
    {
        var wavReader = new WaveReader();
        AudioData audioData;
        using (var wavStream = new MemoryStream(wavData))
            audioData = wavReader.Read(wavStream);

        var adpcm = audioData.GetFormat<GcAdpcmFormat>(new GcAdpcmParameters());
        return BuildBfwav(adpcm);
    }

    public static byte[] ConvertFromWav(string wavPath)
    {
        byte[] wavData = File.ReadAllBytes(wavPath);
        return ConvertFromWav(wavData);
    }

    public static byte[] ConvertToWav(byte[] bfwavData)
    {
        var reader = new BCFstmReader();
        AudioData audioData;
        using (var ms = new MemoryStream(bfwavData))
            audioData = reader.Read(ms);

        var wavWriter = new WaveWriter();
        using var outStream = new MemoryStream();
        wavWriter.WriteToStream(audioData, outStream);
        return outStream.ToArray();
    }

    private static byte[] BuildBfwav(GcAdpcmFormat adpcm)
    {
        int channelCount = adpcm.ChannelCount;
        int sampleCount = adpcm.SampleCount;

        byte[][] channelAudio = new byte[channelCount][];
        for (int i = 0; i < channelCount; i++)
            channelAudio[i] = adpcm.Channels[i].GetAdpcmAudio();

        int audioDataLength = GcAdpcmMath.SampleCountToByteCount(sampleCount);

        int infoContentSize =
            20 +                        // wave info fields (encoding through adjusted_loop_start)
            4 + 8 * channelCount +      // channel reference table
            20 * channelCount +         // channel info entries
            0x2E * channelCount;        // DSP ADPCM info per channel
        int infoBlockSize = AlignUp(8 + infoContentSize, 0x20);

        int infoOffset = HEADER_SIZE;
        int dataOffset = infoOffset + infoBlockSize;

        int dataHeaderSize = 8; // "DATA" + size
        int dataContentStart = AlignUp(dataOffset + dataHeaderSize, DATA_ALIGNMENT);
        int paddingBeforeAudio = dataContentStart - (dataOffset + dataHeaderSize);

        int totalAudioSize = audioDataLength * channelCount;
        int dataBlockSize = dataHeaderSize + paddingBeforeAudio + totalAudioSize;
        int fileSize = dataOffset + dataBlockSize;

        using var ms = new MemoryStream(fileSize);
        using var writer = new BinaryWriter(ms, Encoding.UTF8);

        writer.Write(Encoding.ASCII.GetBytes("FWAV"));
        writer.Write(BOM_LE);
        writer.Write((short)HEADER_SIZE);
        writer.Write(VERSION_10200);
        writer.Write(fileSize);
        writer.Write((short)2); // block count
        writer.Write((short)0); // padding

        writer.Write(TYPE_INFO_BLOCK);
        writer.Write((short)0);
        writer.Write(infoOffset);
        writer.Write(infoBlockSize);

        writer.Write(TYPE_DATA_BLOCK);
        writer.Write((short)0);
        writer.Write(dataOffset);
        writer.Write(dataBlockSize);

        while (ms.Position < HEADER_SIZE)
            writer.Write((byte)0);

        writer.Write(Encoding.ASCII.GetBytes("INFO"));
        writer.Write(infoBlockSize);

        writer.Write((byte)2); // DSP ADPCM encoding
        writer.Write((byte)(adpcm.Looping ? 1 : 0));
        writer.Write((short)0); // padding
        writer.Write(adpcm.SampleRate);
        writer.Write(adpcm.Looping ? adpcm.LoopStart : 0);
        writer.Write(sampleCount);
        writer.Write(adpcm.Looping ? adpcm.UnalignedLoopStart : 0); // adjusted loop start

        long refTableCountPos = ms.Position;
        writer.Write(channelCount);

        long channelRefsStart = ms.Position;
        int refTableSize = 8 * channelCount;
        int channelInfoStartOffset = 4 + refTableSize;

        for (int ch = 0; ch < channelCount; ch++)
        {
            writer.Write(TYPE_WAVE_CHANNEL_INFO);
            writer.Write((short)0);
            int channelInfoOffset = channelInfoStartOffset + 20 * ch;
            writer.Write(channelInfoOffset);
        }

        for (int ch = 0; ch < channelCount; ch++)
        {
            writer.Write(TYPE_SAMPLE_DATA);
            writer.Write((short)0);
            int audioOffsetInData = paddingBeforeAudio + audioDataLength * ch;
            writer.Write(audioOffsetInData);

            writer.Write(TYPE_GC_ADPCM_INFO);
            writer.Write((short)0);
            int channelInfoEntrySize = 20;
            int allChannelInfoSize = channelInfoEntrySize * channelCount;
            int adpcmInfoGlobalOffset = (int)(channelRefsStart + refTableSize) + allChannelInfoSize + 0x2E * ch;
            int thisChannelInfoGlobalOffset = (int)(channelRefsStart + refTableSize) + channelInfoEntrySize * ch;
            writer.Write(adpcmInfoGlobalOffset - thisChannelInfoGlobalOffset);

            writer.Write(0); // unused padding
        }

        // DSP ADPCM info per channel
        for (int ch = 0; ch < channelCount; ch++)
        {
            var channel = adpcm.Channels[ch];

            foreach (short coef in channel.Coefs)
                writer.Write(coef);

            writer.Write(channel.StartContext.PredScale);
            writer.Write(channel.StartContext.Hist1);
            writer.Write(channel.StartContext.Hist2);

            var loopCtx = adpcm.Looping ? channel.LoopContext : channel.StartContext;
            writer.Write(loopCtx.PredScale);
            writer.Write(loopCtx.Hist1);
            writer.Write(loopCtx.Hist2);

            writer.Write((short)0);
        }

        while (ms.Position < infoOffset + infoBlockSize)
            writer.Write((byte)0);

        writer.Write(Encoding.ASCII.GetBytes("DATA"));
        writer.Write(dataBlockSize);

        // Pad to alignment
        while (ms.Position < dataContentStart)
            writer.Write((byte)0);

        for (int ch = 0; ch < channelCount; ch++)
            writer.Write(channelAudio[ch], 0, audioDataLength);

        while (ms.Position < fileSize)
            writer.Write((byte)0);

        return ms.ToArray();
    }

    private static int AlignUp(int value, int alignment) =>
        (value + alignment - 1) / alignment * alignment;
}
