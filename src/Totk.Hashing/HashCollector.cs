using CommunityToolkit.HighPerformance.Buffers;
using Revrs;
using Revrs.Extensions;
using SarcLibrary;
using System.Buffers;
using System.IO.Hashing;
using System.Text.Json;
using Totk.Common.Components;
using Totk.Common.Extensions;
using Totk.Hashing.Models;

namespace Totk.Hashing;

public class HashCollector(string[] sourceFolders)
{
    private const uint SARC_MAGIC = 0x43524153;

    public static readonly JsonSerializerOptions _jsonOptions = new() {
        WriteIndented = true
    };

    public static readonly string[] _ignore = [
        "System/Resource/ResourceSizeTable.Product.rsizetable",
        "Pack/ZsDic.pack",
    ];

    private int _baseVersion = -1;
    private int _tracking = 0;
    private readonly string[] _sourceFolders = sourceFolders;
    private readonly Dictionary<string, HashCollectorEntry> _cache = [];

    public void Save(Stream stream)
    {
        stream.Write(_cache.Count);
        stream.Write(_cache.Count);

        foreach (string str in _ignore) {
            _cache.Remove(str);
        }

        int totalVersionCount = 0;
        foreach ((string key, HashCollectorEntry entry) in _cache) {
            stream.Write(TotkChecksums.GetNameHash(key));
            stream.Write(totalVersionCount);
            stream.Write(entry.Size);
            stream.Write(entry.Hash);

            totalVersionCount += entry.Versions.Count;
        }

        foreach ((string _, HashCollectorEntry entry) in _cache) {
            foreach ((int version, HashCollectorEntry subEntry) in entry.Versions) {
                stream.Write(version);
                stream.Write(subEntry.Size);
                stream.Write(subEntry.Hash);
            }
        }
    }

    public void SaveDebug(Stream output)
    {
        JsonSerializer.Serialize(output, _cache, _jsonOptions);
    }

    public async Task Collect()
    {
        foreach (string folder in _sourceFolders) {
            string zsDicPack = Path.Combine(folder, "Pack", "ZsDic.pack.zs");
            if (!File.Exists(zsDicPack)) {
                return;
            }

            int version = folder.GetRomfsVersionOrDefault();
            if (_baseVersion < 0) {
                _baseVersion = version;
            }

            ZstdExtension.LoadDictionaries(zsDicPack);
            await CollectDiskDirectory(folder, folder, version);

            Console.WriteLine($"\n[{DateTime.Now:t}] Completed {folder}");
            _tracking = 0;
        }
    }

    private async Task CollectDiskDirectory(string directory, string romfs, int version)
    {
        await Parallel.ForEachAsync(Directory.EnumerateFiles(directory), async (file, cancellationToken) => {
            await Task.Run(() => CollectDiskFile(file, romfs, version), cancellationToken);
        });

        await Parallel.ForEachAsync(Directory.EnumerateDirectories(directory), async (folder, cancellationToken) => {
            await CollectDiskDirectory(folder, romfs, version);
        });
    }

    private void CollectDiskFile(string filePath, string romfs, int version)
    {
        string canonical = filePath.ToCanonical(romfs, out RomfsFileAttributes attributes);
        if (attributes.HasFlag(RomfsFileAttributes.HasMcExtension)) {
            // MC files are skipped until
            // decompression is possible
            return;
        }

        using FileStream fs = File.OpenRead(filePath);
        int size = Convert.ToInt32(fs.Length);
        using SpanOwner<byte> buffer = SpanOwner<byte>.Allocate(size);
        fs.Read(buffer.Span);

        if (attributes.HasFlag(RomfsFileAttributes.HasZsExtension)) {
            int decompressedSize = buffer.Span.GetZsDecompressedSize();
            using SpanOwner<byte> decompressed = SpanOwner<byte>.Allocate(decompressedSize);
            buffer.Span.ZsDecompress(decompressed.Span);
            CollectData(canonical, decompressed.Span, version);
            return;
        }

        CollectData(canonical, buffer.Span, version);
    }

    private void CollectData(string canonical, Span<byte> data, int version)
    {
        if (data.Length > 3 && data.Read<uint>() == SARC_MAGIC) {
            RevrsReader reader = new(data);
            ImmutableSarc sarc = new(ref reader);
            foreach ((string sarcFileName, Span<byte> sarcFileData) in sarc) {
                CollectData(sarcFileName, sarcFileData, version);
            }
        }

        CollectChecksum(canonical, data, version);
    }

    private void CollectChecksum(string canonicalFileName, Span<byte> data, int version)
    {
        HashCollectorEntry entry;

        if (data.IsZsCompressed()) {
            int decompressedSize = data.GetZsDecompressedSize();
            using SpanOwner<byte> decompressed = SpanOwner<byte>.Allocate(decompressedSize);
            data.ZsDecompress(decompressed.Span);

            entry = new() {
                Hash = XxHash3.HashToUInt64(decompressed.Span),
                Size = decompressedSize
            };
        }
        else {
            entry = new() {
                Hash = XxHash3.HashToUInt64(data),
                Size = data.Length
            };
        }

        lock (_cache) {
            if (_cache.TryGetValue(canonicalFileName, out HashCollectorEntry? parent)) {
                if ((parent.Versions.Count == 0 && parent != entry) || parent.Versions.Values.Last() != entry) {
                    parent.Versions[version] = entry;
                }

                Console.Write($"\r{++_tracking}");
                return;
            }

            _cache[canonicalFileName] = entry;
        }

        Console.Write($"\r{++_tracking}");
    }
}
