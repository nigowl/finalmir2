using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Server.ConsoleHost;

public static class SpritePreview
{
    public static (int Width, int Height, byte[] Pixels) Decode(byte[] frame)
    {
        if (frame.Length < 8) throw new InvalidDataException("素材帧无效。");
        var width = BinaryPrimitives.ReadInt16LittleEndian(frame); var height = BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(2));
        if (width <= 0 || height <= 0) { width = height = 1; frame = new byte[8]; }
        if (width > 4096 || height > 4096) throw new InvalidDataException("素材预览超过尺寸上限。");
        var pixels = new byte[checked(width * height * 4)];
        if (frame.Length > 8)
        {
            using var gzip = new GZipStream(new MemoryStream(frame, 8, frame.Length - 8), CompressionMode.Decompress);
            gzip.ReadExactly(pixels); if (gzip.ReadByte() != -1) throw new InvalidDataException("像素数据长度无效。");
            for (var index = 0; index < pixels.Length; index += 4) (pixels[index], pixels[index + 2]) = (pixels[index + 2], pixels[index]);
        }
        return (width, height, pixels);
    }
    public static byte[] Png(byte[] frame)
    {
        var image = Decode(frame); return Png(image.Width, image.Height, image.Pixels);
    }
    public static byte[] Png(int width, int height, byte[] pixels)
    {
        if (width < 1 || height < 1 || width > 4096 || height > 4096 || pixels.Length != checked(width * height * 4)) throw new InvalidDataException("预览图片像素无效。");
        using var output = new MemoryStream(); output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height); header[8] = 8; header[9] = 6;
        Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            for (var y = 0; y < height; y++) { zlib.WriteByte(0); zlib.Write(pixels.AsSpan(y * width * 4, width * 4)); }
        Chunk(output, "IDAT", compressed.ToArray()); Chunk(output, "IEND", []); return output.ToArray();
    }
    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, data.Length); output.Write(number);
        var tag = Encoding.ASCII.GetBytes(type); output.Write(tag); output.Write(data);
        uint crc = 0xffffffff;
        foreach (var value in tag.Concat(data))
        { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320 : 0); }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
    }
}
