namespace NtfsRescue.Core;

/// <summary>
/// Dekompresia LZNT1 (NTFS kompresia). Vstup je jeden NTFS compression unit
/// (zvycajne 16 klastrov), vystup maxOutput bajtov.
/// </summary>
public static class Lznt1
{
    public static byte[] Decompress(byte[] input, int maxOutput)
    {
        if (maxOutput <= 0) return Array.Empty<byte>();

        var output = new List<byte>(Math.Min(maxOutput, 1 << 16));
        int pos = 0;

        while (pos + 2 <= input.Length && output.Count < maxOutput)
        {
            int header = input[pos] | (input[pos + 1] << 8);
            pos += 2;
            if (header == 0) break;

            int size = (header & 0x0FFF) + 1;
            bool compressed = (header & 0x8000) != 0;

            if (pos + size > input.Length)
                size = input.Length - pos;
            if (size <= 0) break;

            if (!compressed)
            {
                int take = Math.Min(size, maxOutput - output.Count);
                for (int i = 0; i < take; i++) output.Add(input[pos + i]);
            }
            else
            {
                DecompressChunk(input, pos, size, output, maxOutput);
            }

            pos += size;
        }

        return output.ToArray();
    }

    private static void DecompressChunk(byte[] input, int offset, int length, List<byte> output, int maxOutput)
    {
        int end = offset + length;
        int chunkLength = 0;
        int split = 12;
        int mask = (1 << 12) - 1;
        int threshold = 16;

        int p = offset;
        while (p < end && output.Count < maxOutput)
        {
            byte flags = input[p++];

            for (int bit = 0; bit < 8; bit++)
            {
                if (p >= end || output.Count >= maxOutput) return;

                if ((flags & (1 << bit)) == 0)
                {
                    output.Add(input[p++]);
                    chunkLength++;
                }
                else
                {
                    if (p + 2 > end) return;

                    int token = input[p] | (input[p + 1] << 8);
                    p += 2;

                    int matchLength = (token & mask) + 3;
                    int matchOffset = (token >> split) + 1;

                    if (matchOffset <= 0 || matchOffset > chunkLength) return;

                    for (int i = 0; i < matchLength; i++)
                    {
                        if (output.Count >= maxOutput) return;
                        int src = output.Count - matchOffset;
                        output.Add(output[src]);
                        chunkLength++;
                    }
                }

                while (chunkLength > threshold && split > 4)
                {
                    split--;
                    mask = (1 << split) - 1;
                    threshold <<= 1;
                }
            }
        }
    }
}
