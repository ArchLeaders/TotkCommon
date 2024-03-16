using CommunityToolkit.HighPerformance.Buffers;
using Revrs.Extensions;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Totk.Common.Extensions;
using Totk.Common.Models;

namespace Totk.Common.Components;

public class TotkChecksums
{
    private readonly int _baseVersion;
    private readonly FrozenDictionary<ulong, ChecksumEntry> _baseEntries;
    private readonly ChecksumEntry[] _entries = [];

    public static TotkChecksums FromFile(string checksumCachePath)
    {
        using FileStream fs = File.OpenRead(checksumCachePath);
        return FromStream(fs);
    }

    public static TotkChecksums FromStream(Stream checksumCacheStream)
    {
        int baseVersion = checksumCacheStream.Read<int>();
        int baseEntryCount = checksumCacheStream.Read<int>();

        Dictionary<ulong, ChecksumEntry> baseEntries = [];
        for (int i = 0; i < baseEntryCount; i++) {
            ulong key = checksumCacheStream.Read<ulong>();
            baseEntries[key] = checksumCacheStream.Read<ChecksumEntry>();
        }

        long entryCount = (checksumCacheStream.Length - checksumCacheStream.Position) / Unsafe.SizeOf<ChecksumEntry>();
        ChecksumEntry[] entries = new ChecksumEntry[entryCount];

        for (int i = 0; i < entryCount; i++) {
            entries[i] = checksumCacheStream.Read<ChecksumEntry>();
        }

        return new(baseVersion, baseEntries, entries);
    }

    public static ulong GetNameHash(ReadOnlySpan<char> canonicalFileName)
    {
        ReadOnlySpan<byte> canonicalFileNameBytes = MemoryMarshal.Cast<char, byte>(canonicalFileName);
        return XxHash3.HashToUInt64(canonicalFileNameBytes);
    }

    private TotkChecksums(int baseVersion, Dictionary<ulong, ChecksumEntry> baseEntries, ChecksumEntry[] entries)
    {
        _baseVersion = baseVersion;
        _baseEntries = baseEntries.ToFrozenDictionary();
        _entries = entries;
    }

    public bool IsFileVanilla(string filePath, string baseRomfsFolder)
    {
        int romfsVersion = baseRomfsFolder.GetRomfsVersionOrDefault();
        return IsFileVanilla(filePath, baseRomfsFolder, romfsVersion);
    }

    public bool IsFileVanilla(string filePath, string baseRomfsFolder, int romfsVersion)
    {
        ReadOnlySpan<char> canonicalFileName = filePath.ToCanonical(baseRomfsFolder, out RomfsFileAttributes romfsFileAttributes);
        return IsFileVanilla(canonicalFileName, new FileInfo(filePath), romfsFileAttributes, romfsVersion);
    }

    public bool IsFileVanilla(ReadOnlySpan<char> canonicalFileName, FileInfo fileInfo, RomfsFileAttributes romfsFileAttributes, int romfsVersion)
    {
        if (!Lookup(canonicalFileName, romfsVersion, out ChecksumEntry checksumEntry)) {
            return false;
        }

        bool isZsCompressed = romfsFileAttributes.HasFlag(RomfsFileAttributes.HasZsExtension);

        // Check the file size of decompressed
        // targets before reading the file.
        if (isZsCompressed == false && checksumEntry.Size != fileInfo.Length) {
            return false;
        }

        int decompresedSize = -1;
        using FileStream fs = fileInfo.OpenRead();

        if (isZsCompressed) {
            // Check the file size of compressed
            // targets before reading the file.
            if ((decompresedSize = fs.GetZsDecompressedSize()) != checksumEntry.Size) {
                return false;
            }

            fs.Seek(0, SeekOrigin.Begin);
        }

        int size = Convert.ToInt32(fileInfo.Length);
        using SpanOwner<byte> buffer = SpanOwner<byte>.Allocate(size);
        fs.Read(buffer.Span);

        bool result = CheckData(buffer.Span, checksumEntry, decompresedSize);
        return result;
    }

    public bool IsFileVanilla(ReadOnlySpan<char> canonicalFileName, Span<byte> fileData, int romfsVersion)
    {
        if (!Lookup(canonicalFileName, romfsVersion, out ChecksumEntry entry)) {
            return false;
        }

        int decompressedSize = -1;
        if (fileData.IsZsCompressed() && (decompressedSize = fileData.GetZsDecompressedSize()) != entry.Size) {
            return false;
        }
        else if (entry.Size != fileData.Length) {
            return false;
        }

        return CheckData(fileData, entry, decompressedSize);
    }

    private static bool CheckData(Span<byte> data, in ChecksumEntry entry, int decompressedSize)
    {
        if (decompressedSize > 0) {
            using SpanOwner<byte> buffer = SpanOwner<byte>.Allocate(decompressedSize);
            data.ZsDecompress(buffer.Span);
            bool isMatch = XxHash3.HashToUInt64(buffer.Span) == entry.Checksum;
            return isMatch;
        }

        return XxHash3.HashToUInt64(data) == entry.Checksum;
    }

    private bool Lookup(ReadOnlySpan<char> canonicalFileName, int version, [MaybeNullWhen(false)] out ChecksumEntry entry)
    {
        ulong key = GetNameHash(canonicalFileName);

        if (_baseEntries.TryGetValue(key, out entry) == false) {
            return false;
        }

        if (version == _baseVersion) {
            return true;
        }

        int index = entry.Info.FirstVersionIndex;
        ChecksumEntry first = _entries[index];
        ChecksumEntry next;

        while ((next = _entries[index++]).Info.Version <= version && next.Info.Version >= first.Info.Version) {
            entry = next;
        }

        return true;
    }
}
