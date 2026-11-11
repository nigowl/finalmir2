using System.Buffers.Binary;
using Client.MirObjects;

namespace Server.ConsoleHost;

public sealed class GameAssets(string assetRoot)
{

    public static string Resolve(string root, string name, string extension)
    {
        var path = Path.GetFullPath(Path.Combine(root, name.Replace('\\', '/') + extension));
        if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Asset path is outside its data directory.");
        if (!File.Exists(path))
        {
            // The Windows client treats library and map names as case-insensitive.
            var current = Path.GetFullPath(root);
            foreach (var part in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar))
            {
                if (!Directory.Exists(current)) throw new FileNotFoundException("Asset not found.", path);
                current = Directory.EnumerateFileSystemEntries(current).FirstOrDefault(p => string.Equals(Path.GetFileName(p), part, StringComparison.OrdinalIgnoreCase))
                    ?? throw new FileNotFoundException("Asset not found.", path);
            }
            path = current;
        }
        return path;
    }

    public string MapPath(string name) => Resolve(Path.Combine(assetRoot, "maps"), name, ".map");

    public string SoundPath(string name)
    {
        if (Path.GetExtension(name).ToLowerInvariant() is not (".wav" or ".mp3")) throw new InvalidDataException("Unsupported sound extension.");
        return Resolve(Path.Combine(assetRoot, "sounds"), name, "");
    }

    public byte[] Frame(string library, int index)
    {
        var path = Resolve(Path.Combine(assetRoot, "libraries"), library, ".Lib");
        using var reader = new BinaryReader(File.OpenRead(path));
        var version = reader.ReadInt32();
        var count = reader.ReadInt32();
        if (version is < 2 or > 3 || count is < 0 or > 1000000) throw new InvalidDataException("Unsupported sprite library version.");
        if (index < 0 || index >= count) throw new FileNotFoundException("Sprite index not found.");
        var start = version >= 3 ? 12L : 8L;
        reader.BaseStream.Position = start + index * 4L;
        var offset = reader.ReadInt32();
        if (offset == 0) return new byte[8];
        if (offset < start + count * 4L || offset > reader.BaseStream.Length - 17)
            throw new InvalidDataException("Invalid sprite offset.");
        reader.BaseStream.Position = offset;
        var header = reader.ReadBytes(8);
        var width = BinaryPrimitives.ReadInt16LittleEndian(header);
        var height = BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(2));
        if (width == 0 || height == 0) return new byte[8];
        if (width < 0 || height < 0) throw new InvalidDataException("Invalid sprite dimensions.");
        if (width > 4096 || height > 4096) throw new InvalidDataException("Sprite exceeds size limit.");
        reader.BaseStream.Position += 5;
        var length = reader.ReadInt32();
        if (length < 0 || length > 16 * 1024 * 1024 || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid sprite data length.");
        var bytes = new byte[8 + length];
        header.CopyTo(bytes, 0);
        reader.BaseStream.ReadExactly(bytes.AsSpan(8));
        return bytes;
    }

    public object Frames(string library)
    {
        var path = Resolve(Path.Combine(assetRoot, "libraries"), library, ".Lib");
        using var reader = new BinaryReader(File.OpenRead(path));
        var version = reader.ReadInt32();
        var count = reader.ReadInt32();
        if (version is < 2 or > 3 || count is < 0 or > 1000000) throw new InvalidDataException("Invalid sprite library header.");
        var frames = new FrameSet();
        if (version >= 3)
        {
            reader.BaseStream.Position = reader.ReadInt32();
            var total = reader.ReadInt32();
            if (total is < 0 or > 256) throw new InvalidDataException("Invalid sprite frame count.");
            for (var i = 0; i < total; i++) frames.Add((MirAction)reader.ReadByte(), new Frame(reader));
        }
        if (frames.Count == 0)
            frames = library.StartsWith("NPC/") ? FrameSet.DefaultNPC :
                library.StartsWith("Monster/") ? FrameSet.DefaultMonster : FrameSet.Player;
        return new
        {
            count,
            frames = frames.ToDictionary(pair => pair.Key.ToString(), pair => new
            {
                start = pair.Value.Start, count = pair.Value.Count, stride = pair.Value.OffSet,
                interval = pair.Value.Interval, reverse = pair.Value.Reverse, blend = pair.Value.Blend,
                effectStart = pair.Value.EffectStart, effectCount = pair.Value.EffectCount,
                effectStride = pair.Value.EffectOffSet, effectInterval = pair.Value.EffectInterval
            })
        };
    }
}
