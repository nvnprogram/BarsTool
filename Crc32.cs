namespace BarsTool;

public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            table[i] = crc;
        }
        return table;
    }

    public static uint Compute(string text)
    {
        uint crc = 0xFFFFFFFF;
        foreach (char c in text)
            crc = (crc >> 8) ^ Table[(crc ^ (byte)c) & 0xFF];
        return crc ^ 0xFFFFFFFF;
    }

    public static uint Compute(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
            crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        return crc ^ 0xFFFFFFFF;
    }
}
