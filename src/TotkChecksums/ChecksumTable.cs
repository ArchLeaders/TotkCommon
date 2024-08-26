using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using System.IO.Hashing;
using System.Text;
using TotkCommon.Extensions;

namespace TotkChecksums;

public class ChecksumTable
{
    public Dictionary<ulong, ulong> Checksums { get; set; } = [];
    public Dictionary<byte[], ulong> Overflow { get; set; } = [];

    public static ChecksumTable FromGameDump(string directory)
    {
        Dictionary<ulong, ulong> checksums = [];
        Dictionary<byte[], ulong> overflow = [];
        int i = 0;

        string[] files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);

        foreach (var file in files) {
            // Make sure the file is located and opened
            // before normalizing the path for hashing.
            using FileStream fs = File.OpenRead(file);
            int size = Convert.ToInt32(fs.Length);
            using SpanOwner<byte> dataOwner = SpanOwner<byte>.Allocate(size);
            fs.Read(dataOwner.Span);
            ulong checksum = XxHash3.HashToUInt64(dataOwner.Span);

            ReadOnlySpan<char> name = PathExtension.GetRelativePathUnchecked(directory, file);
            name.NormalizeInline();
            ulong key = XxHash3.HashToUInt64(name.Cast<char, byte>());

            if (!checksums.TryAdd(key, checksum)) {
                int utf8NameSize = Encoding.UTF8.GetByteCount(name);
                byte[] utf8Name = new byte[utf8NameSize];
                Encoding.UTF8.GetBytes(name, utf8Name);
                overflow.Add(utf8Name, key);
            }

            Console.Write($"\r{++i}/{files.Length}");
        }

        return new ChecksumTable {
            Checksums = checksums,
            Overflow = overflow
        };
    }

    public void WriteBinary(Stream stream)
    {
        stream.Write(Checksums.Count);
        foreach (var (key, checksum) in Checksums) {
            stream.Write(key);
            stream.Write(checksum);
        }

        stream.Write(Overflow.Count);
        foreach (var (key, checksum) in Overflow) {
            stream.Write(Convert.ToInt16(key.Length));
            stream.Write(key);
            stream.Write(checksum);
        }
    }
}
