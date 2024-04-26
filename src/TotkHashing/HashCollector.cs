using CommunityToolkit.HighPerformance.Buffers;
using Revrs;
using Revrs.Extensions;
using SarcLibrary;
using System.Buffers;
using System.IO.Hashing;
using System.Text.Json;
using TotkCommon;
using TotkCommon.Components;
using TotkCommon.Extensions;
using TotkHashing.Models;
using HashVersions = System.Collections.Generic.List<TotkHashing.Models.HashCollectorEntry>;

namespace TotkHashing;

public class HashCollector(string[] sourceFolders)
{
    private const uint SARC_MAGIC = 0x43524153;

    public static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public static readonly string[] _ignore = [
        "System/Resource/ResourceSizeTable.Product.rsizetable",
        "Pack/ZsDic.pack",
    ];

    private int _baseVersion = -1;
    private int _tracking = 0;
    private readonly string[] _sourceFolders = sourceFolders;
    private readonly Dictionary<string, HashVersions> _cache = [];

    public void Save(Stream stream)
    {
        foreach (string str in _ignore)
        {
            _cache.Remove(str);
        }

        stream.Write(_baseVersion);
        stream.Write(_cache.Count);

        foreach ((string key, HashVersions versions) in _cache)
        {
            stream.Write(TotkChecksums.GetNameHash(key));
            stream.Write(versions.Count);

            foreach (var (version, size, hash) in versions)
            {
                stream.Write(version);
                stream.Write(size);
                stream.Write(hash);
            }
        }
    }

    public void SaveDebug(Stream output)
    {
        JsonSerializer.Serialize(output, _cache, _jsonOptions);
    }

    public async Task Collect()
    {
        foreach (string folder in _sourceFolders)
        {
            string zsDicPack = Path.Combine(folder, "Pack", "ZsDic.pack.zs");
            if (!File.Exists(zsDicPack))
            {
                return;
            }

            int version = folder.GetRomfsVersionOrDefault();
            if (_baseVersion < 0)
            {
                _baseVersion = version;
            }

            Zstd.Shared.LoadDictionaries(zsDicPack);
            await CollectDiskDirectory(folder, folder, version);

            Console.WriteLine($"\n[{DateTime.Now:t}] Completed {folder}");
            _tracking = 0;
        }
    }

    private async Task CollectDiskDirectory(string directory, string romfs, int version)
    {
        await Parallel.ForEachAsync(Directory.EnumerateFiles(directory), async (file, cancellationToken) =>
        {
            await Task.Run(() => CollectDiskFile(file, romfs, version), cancellationToken);
        });

        await Parallel.ForEachAsync(Directory.EnumerateDirectories(directory), async (folder, cancellationToken) =>
        {
            await CollectDiskDirectory(folder, romfs, version);
        });
    }

    private void CollectDiskFile(string filePath, string romfs, int version)
    {
        string canonical = filePath.ToCanonical(romfs, out RomfsFileAttributes attributes).ToString();
        if (attributes.HasFlag(RomfsFileAttributes.HasMcExtension))
        {
            // MC files are skipped until
            // decompression is possible
            return;
        }

        using FileStream fs = File.OpenRead(filePath);
        int size = Convert.ToInt32(fs.Length);
        using SpanOwner<byte> buffer = SpanOwner<byte>.Allocate(size);
        fs.Read(buffer.Span);

        if (attributes.HasFlag(RomfsFileAttributes.HasZsExtension))
        {
            int decompressedSize = Zstd.GetDecompressedSize(buffer.Span);
            using SpanOwner<byte> decompressed = SpanOwner<byte>.Allocate(decompressedSize);
            Zstd.Shared.Decompress(buffer.Span, decompressed.Span);
            CollectData(canonical, decompressed.Span, version);
            return;
        }

        CollectData(canonical, buffer.Span, version);
    }

    private void CollectData(string canonical, Span<byte> data, int version)
    {
        if (data.Length > 3 && data.Read<uint>() == SARC_MAGIC)
        {
            ReadOnlySpan<char> ext = Path.GetExtension(canonical.AsSpan());
            RevrsReader reader = new(data);
            ImmutableSarc sarc = new(ref reader);
            foreach ((string sarcFileName, Span<byte> sarcFileData) in sarc)
            {
                switch (ext)
                {
                    case ".pack":
                        {
                            CollectData(sarcFileName, sarcFileData, version);
                            break;
                        }
                    default:
                        {
                            CollectData($"{canonical}/{sarcFileName}", sarcFileData, version);
                            break;
                        }
                }
            }
        }

        CollectChecksum(canonical, data, version);
    }

    private void CollectChecksum(string canonicalFileName, Span<byte> data, int version)
    {
        HashCollectorEntry entry;
        entry.Version = version;

        if (Zstd.IsCompressed(data))
        {
            int decompressedSize = Zstd.GetDecompressedSize(data);
            using SpanOwner<byte> decompressed = SpanOwner<byte>.Allocate(decompressedSize);
            Zstd.Shared.Decompress(data, decompressed.Span);

            entry.Hash = XxHash3.HashToUInt64(decompressed.Span);
            entry.Size = decompressedSize;
        }
        else
        {
            entry.Hash = XxHash3.HashToUInt64(data);
            entry.Size = data.Length;
        }

        lock (_cache)
        {
            if (_cache.TryGetValue(canonicalFileName, out HashVersions? versions))
            {
                var (_, size, hash) = versions[^1];
                if (size != entry.Size || hash != entry.Hash)
                {
                    versions.Add(entry);
                }

                Console.Write($"\r{++_tracking}");
                return;
            }

            _cache[canonicalFileName] = [entry];
        }

        Console.Write($"\r{++_tracking}");
    }
}
