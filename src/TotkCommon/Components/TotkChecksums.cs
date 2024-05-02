using CommunityToolkit.HighPerformance.Buffers;
using Revrs.Extensions;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using TotkCommon.Extensions;
using TotkCommon.Models;

namespace TotkCommon.Components;

public class TotkChecksums
{
    private readonly int _version;
    private readonly FrozenDictionary<ulong, ChecksumEntry[]> _entries;

    public static TotkChecksums FromFile(string checksumCachePath)
    {
        using FileStream fs = File.OpenRead(checksumCachePath);
        return FromStream(fs);
    }

    public static TotkChecksums FromStream(Stream stream)
    {
        int version = stream.Read<int>();
        int entryCount = stream.Read<int>();

        Dictionary<ulong, ChecksumEntry[]> entries = [];
        for (int i = 0; i < entryCount; i++)
        {
            ulong key = stream.Read<ulong>();
            int count = stream.Read<int>();
            ChecksumEntry[] versions = new ChecksumEntry[count];
            for (int j = 0; j < count; j++)
            {
                versions[j] = stream.Read<ChecksumEntry>();
            }

            entries[key] = versions;
        }

        return new(version, entries);
    }

    public static ulong GetNameHash(ReadOnlySpan<char> canonicalFileName)
    {
        ReadOnlySpan<byte> canonicalFileNameBytes = MemoryMarshal.Cast<char, byte>(canonicalFileName);
        return XxHash3.HashToUInt64(canonicalFileNameBytes);
    }

    private TotkChecksums(int version, Dictionary<ulong, ChecksumEntry[]> entries)
    {
        _version = version;
        _entries = entries.ToFrozenDictionary();
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
        if (!Lookup(canonicalFileName, romfsVersion, out ChecksumEntry checksumEntry))
        {
            return false;
        }

        bool isZsCompressed = romfsFileAttributes.HasFlag(RomfsFileAttributes.HasZsExtension);

        // Check the file size of decompressed
        // targets before reading the file.
        if (isZsCompressed == false && checksumEntry.Size != fileInfo.Length)
        {
            return false;
        }

        int decompresedSize = -1;
        using FileStream fs = fileInfo.OpenRead();

        if (isZsCompressed)
        {
            // Check the file size of compressed
            // targets before reading the file.
            if ((decompresedSize = Zstd.GetDecompressedSize(fs)) != checksumEntry.Size)
            {
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
        if (!Lookup(canonicalFileName, romfsVersion, out ChecksumEntry entry))
        {
            return false;
        }

        int decompressedSize = -1;
        if (Zstd.IsCompressed(fileData) && (decompressedSize = Zstd.GetDecompressedSize(fileData)) != entry.Size)
        {
            return false;
        }
        else if (entry.Size != fileData.Length)
        {
            return false;
        }

        return CheckData(fileData, entry, decompressedSize);
    }

    private static bool CheckData(Span<byte> data, in ChecksumEntry entry, int decompressedSize)
    {
        if (decompressedSize > 0)
        {
            using SpanOwner<byte> buffer = SpanOwner<byte>.Allocate(decompressedSize);
            Zstd.Shared.Decompress(data, buffer.Span);
            bool isMatch = XxHash3.HashToUInt64(buffer.Span) == entry.Hash;
            return isMatch;
        }

        return XxHash3.HashToUInt64(data) == entry.Hash;
    }

    private bool Lookup(ReadOnlySpan<char> canonicalFileName, int version, [MaybeNullWhen(false)] out ChecksumEntry entry)
    {
        ulong key = GetNameHash(canonicalFileName);

        if (_entries.TryGetValue(key, out ChecksumEntry[]? entries) == false)
        {
            entry = default;
            return false;
        }

        entry = entries[0];
        if (version == _version)
        {
            return true;
        }

        for (int i = 1; i < entries.Length; i++)
        {
            ref ChecksumEntry next = ref entries[i];
            if (next.Version > version)
            {
                break;
            }

            entry = next;
        }

        return true;
    }
}
