using System.Buffers.Binary;
using System.IO.Compression;

namespace ImageCpuDemo;

internal static class PngValidator
{
    internal static void Validate(string path, int width, int height)
    {
        using var file = File.OpenRead(path);
        Span<byte> signature = stackalloc byte[8];
        file.ReadExactly(signature);
        if (!signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidDataException("The backend output is not a PNG.");

        using var compressed = new MemoryStream();
        var channels = 0;
        var ended = false;
        Span<byte> header = stackalloc byte[8];
        Span<byte> checksum = stackalloc byte[4];
        while (file.Position < file.Length)
        {
            file.ReadExactly(header);
            var length = BinaryPrimitives.ReadInt32BigEndian(header);
            if (length < 0 || length > 64 * 1024 * 1024 || length > file.Length - file.Position - 4)
                throw new InvalidDataException("Invalid PNG chunk length.");
            var data = new byte[length];
            file.ReadExactly(data);
            file.ReadExactly(checksum);
            if (Crc32(header[4..], data) != BinaryPrimitives.ReadUInt32BigEndian(checksum))
                throw new InvalidDataException("PNG checksum failed; the output is corrupt.");
            var type = System.Text.Encoding.ASCII.GetString(header[4..]);
            if (channels == 0 && type != "IHDR")
                throw new InvalidDataException("PNG is missing its initial IHDR.");
            switch (type)
            {
                case "IHDR":
                    if (channels != 0 || length != 13 ||
                        BinaryPrimitives.ReadInt32BigEndian(data) != width ||
                        BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4)) != height ||
                        data[8] != 8 || data[9] is not (2 or 6) || data[10] != 0 || data[11] != 0 || data[12] != 0)
                        throw new InvalidDataException($"Expected a non-interlaced {width}x{height} RGB/RGBA PNG.");
                    channels = data[9] == 2 ? 3 : 4;
                    break;
                case "IDAT":
                    compressed.Write(data);
                    break;
                case "IEND":
                    if (length != 0 || compressed.Length == 0 || file.Position != file.Length)
                        throw new InvalidDataException("Invalid PNG end or missing pixel data.");
                    ended = true;
                    break;
            }
        }
        if (!ended)
            throw new InvalidDataException("PNG is incomplete (no IEND).");
        compressed.Position = 0;
        using var pixels = new ZLibStream(compressed, CompressionMode.Decompress);
        var row = new byte[checked(width * channels)];
        for (var y = 0; y < height; y++)
        {
            var filter = pixels.ReadByte();
            if (filter is < 0 or > 4)
                throw new InvalidDataException("PNG has a missing or invalid scanline.");
            pixels.ReadExactly(row);
        }
        if (pixels.ReadByte() != -1)
            throw new InvalidDataException("PNG pixel count does not match its dimensions.");
    }

    internal static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in type)
            Update(value);
        foreach (var value in data)
            Update(value);
        return ~crc;

        void Update(byte value)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0u);
        }
    }
}
