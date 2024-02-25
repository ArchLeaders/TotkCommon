using Revrs;
using Revrs.Extensions;
using SarcLibrary;
using System.Buffers;
using ZstdSharp;

namespace Totk.Common.Extensions;

public static class ZstdExtension
{
    private const uint ZSTD_MAGIC = 0xFD2FB528;
    private const uint DICT_MAGIC = 0xEC30A437;
    private const uint SARC_MAGIC = 0x43524153;

    private static readonly Decompressor _defaultDecompressor = new();
    private static readonly Dictionary<int, Decompressor> _decompressors = [];
    private static readonly Compressor _defaultCompressor = new();
    private static readonly Dictionary<int, Compressor> _compressors = [];

    public static void LoadDictionaries(string file)
    {
        using FileStream fs = File.OpenRead(file);
        int size = Convert.ToInt32(fs.Length);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(size);
        Span<byte> data = buffer.AsSpan()[..size];
        fs.Read(data);
        LoadDictionaries(data);
        ArrayPool<byte>.Shared.Return(buffer);
    }

    public static void LoadDictionaries(Span<byte> data)
    {
        byte[]? decompressedBuffer = null;

        if (data.IsZsCompressed()) {
            int decompressedSize = data.GetZsDecompressedSize();
            decompressedBuffer = ArrayPool<byte>.Shared.Rent(decompressedSize);
            Span<byte> decompressed = decompressedBuffer.AsSpan()[..decompressedSize];
            data.ZsDecompress(decompressed);
            data = decompressed;
        }

        if (TryLoadDictionary(data)) {
            return;
        }

        if (data.Length < 8 || data.Read<uint>() != SARC_MAGIC) {
            return;
        }

        RevrsReader reader = new(data);
        ImmutableSarc sarc = new(ref reader);
        foreach ((string _, Span<byte> sarcFileData) in sarc) {
            TryLoadDictionary(sarcFileData);
        }

        if (decompressedBuffer is not null) {
            ArrayPool<byte>.Shared.Return(decompressedBuffer);
        }
    }

    public static byte[] ZsDecompress(this Span<byte> data)
    {
        return ((ReadOnlySpan<byte>)data).ZsDecompress(out _);
    }

    public static byte[] ZsDecompress(this Span<byte> data, out int zsDictionaryId)
    {
        return ((ReadOnlySpan<byte>)data).ZsDecompress(out zsDictionaryId);
    }

    public static byte[] ZsDecompress(this ReadOnlySpan<byte> data)
    {
        return data.ZsDecompress(out _);
    }

    public static byte[] ZsDecompress(this ReadOnlySpan<byte> data, out int zsDictionaryId)
    {
        int size = data.GetZsDecompressedSize();
        byte[] result = new byte[size];
        data.ZsDecompress(result, out zsDictionaryId);
        return result;
    }

    public static void ZsDecompress(this Span<byte> data, Span<byte> dst)
    {
        ((ReadOnlySpan<byte>)data).ZsDecompress(dst, out _);
    }

    public static void ZsDecompress(this Span<byte> data, Span<byte> dst, out int zsDictionaryId)
    {
        ((ReadOnlySpan<byte>)data).ZsDecompress(dst, out zsDictionaryId);
    }

    public static void ZsDecompress(this ReadOnlySpan<byte> data, Span<byte> dst)
    {
        data.ZsDecompress(dst, out _);
    }

    public static void ZsDecompress(this ReadOnlySpan<byte> data, Span<byte> dst, out int zsDictionaryId)
    {
        if (!data.IsZsCompressed()) {
            zsDictionaryId = -1;
            return;
        }

        zsDictionaryId = GetDictionaryId(data);
        if (_decompressors.TryGetValue(zsDictionaryId, out Decompressor? decompressor)) {
            lock (_decompressors) {
                decompressor.Unwrap(data, dst);
            }

            return;
        }

        lock (_defaultDecompressor) {
            _defaultDecompressor.Unwrap(data, dst);
        }
    }

    public static Span<byte> ZsCompress(this ReadOnlySpan<byte> data, int zsDictionaryId = -1)
    {
        return data.ZsCompress(out int size, zsDictionaryId).AsSpan()[..size];
    }

    public static byte[] ZsCompress(this ReadOnlySpan<byte> data, out int size, int zsDictionaryId = -1)
    {
        int bounds = Compressor.GetCompressBound(data.Length);
        byte[] result = new byte[bounds];
        size = data.ZsCompress(result, zsDictionaryId);
        return result;
    }

    public static int ZsCompress(this ReadOnlySpan<byte> data, Span<byte> dst, int zsDictionaryId = -1)
    {
        return _compressors.TryGetValue(zsDictionaryId, out Compressor? compressor)
            ? compressor.Wrap(data, dst)
            : _defaultCompressor.Wrap(data, dst);
    }

    public static bool IsZsCompressed(this Span<byte> data)
    {
        return data.Length > 3 && data.Read<uint>() == ZSTD_MAGIC;
    }

    public static bool IsZsCompressed(this ReadOnlySpan<byte> data)
    {
        return data.Length > 3 && data.Read<uint>() == ZSTD_MAGIC;
    }

    public static int GetZsDecompressedSize(this string file)
    {
        using FileStream fs = File.OpenRead(file);
        return fs.GetZsDecompressedSize();
    }

    public static int GetZsDecompressedSize(this Stream stream)
    {
        Span<byte> header = stackalloc byte[14];
        stream.Read(header);
        return GetFrameContentSize(header);
    }

    public static int GetZsDecompressedSize(this Span<byte> data)
    {
        return GetFrameContentSize(data);
    }

    public static int GetZsDecompressedSize(this ReadOnlySpan<byte> data)
    {
        return GetFrameContentSize(data);
    }

    private static bool TryLoadDictionary(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 8 || buffer.Read<uint>() != DICT_MAGIC) {
            return false;
        }

        Decompressor decompressor = new();
        decompressor.LoadDictionary(buffer);
        _decompressors[buffer[4..8].Read<int>()] = decompressor;

        Compressor compressor = new();
        compressor.LoadDictionary(buffer);
        _compressors[buffer[4..8].Read<int>()] = compressor;

        return true;
    }

    private static int GetFrameContentSize(ReadOnlySpan<byte> buffer)
    {
        byte descriptor = buffer[4];
        int windowDescriptorSize = ((descriptor & 0b00100000) >> 5) ^ 0b1;
        int dictionaryIdFlag = descriptor & 0b00000011;
        int frameContentFlag = descriptor >> 6;

        int offset = dictionaryIdFlag switch {
            0x0 => 5 + windowDescriptorSize,
            0x1 => 5 + windowDescriptorSize + 1,
            0x2 => 5 + windowDescriptorSize + 2,
            0x3 => 5 + windowDescriptorSize + 4,
            _ => throw new OverflowException("""
                Two bits cannot exceed 0x3, something terrible has happened!
                """)
        };

        return frameContentFlag switch {
            0x0 => buffer[offset],
            0x1 => buffer[offset..].Read<ushort>() + 0x100,
            0x2 => buffer[offset..].Read<int>(),
            _ => throw new NotSupportedException("""
                64-bit file sizes are not supported.
                """)
        };
    }

    private static int GetDictionaryId(ReadOnlySpan<byte> buffer)
    {
        byte descriptor = buffer[4];
        int windowDescriptorSize = ((descriptor & 0b00100000) >> 5) ^ 0b1;
        int dictionaryIdFlag = descriptor & 0b00000011;

        return dictionaryIdFlag switch {
            0x0 => -1,
            0x1 => buffer[5 + windowDescriptorSize],
            0x2 => buffer[(5 + windowDescriptorSize)..].Read<ushort>(),
            0x3 => buffer[(5 + windowDescriptorSize)..].Read<int>(),
            _ => throw new OverflowException("""
                Two bits cannot exceed 0x3, something terrible has happened!
                """)
        };
    }
}
