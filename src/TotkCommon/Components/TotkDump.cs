using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using Revrs;
using System.Collections.Frozen;
using System.IO.Hashing;
using TotkCommon.Extensions;

namespace TotkCommon.Components;

// ReSharper disable NotAccessedPositionalProperty.Global, UnusedType.Global, UnusedMember.Global

public static class TotkDump
{
    public static TotkDumpResults CheckIntegrity(string input, Span<byte> tableData, Action<int, int>? updateCallback = null, bool breakAfterFirstBadFile = false)
    {
        FrozenDictionary<ulong, ulong> checksumTable = ParseChecksumTable(tableData);

        List<string> badFiles = [];
        List<string> extraFiles = [];
        string[] files = Directory.GetFiles(input, "*.*", SearchOption.AllDirectories);

        int i = 0;
        int total = checksumTable.Count;

        foreach (string file in files) {
            // Make sure the file is located and opened
            // before normalizing the path for hashing.
            using FileStream fs = File.OpenRead(file);

            ReadOnlySpan<char> name = PathExtension.GetRelativePathUnchecked(input, file);
            name.NormalizeInline();
            ulong key = XxHash3.HashToUInt64(name.Cast<char, byte>());

            if (!checksumTable.TryGetValue(key, out ulong storedDatachecksum)) {
                extraFiles.Add(file);
                continue;
            }

            int size = Convert.ToInt32(fs.Length);
            using SpanOwner<byte> dataOwner = SpanOwner<byte>.Allocate(size);
            _ = fs.Read(dataOwner.Span);
            ulong currentDataChecksum = XxHash3.HashToUInt64(dataOwner.Span);

            if (currentDataChecksum == storedDatachecksum) {
                updateCallback?.Invoke(++i, total);
                continue;
            }

            badFiles.Add(file);
            if (breakAfterFirstBadFile) {
                break;
            }
        }

        int missingFiles = checksumTable.Count - files.Length;
        return new TotkDumpResults(
            badFiles.Count == 0 && missingFiles <= 0,
            badFiles,
            extraFiles,
            missingFiles
        );
    }

    private static FrozenDictionary<ulong, ulong> ParseChecksumTable(Span<byte> tableData)
    {
        RevrsReader reader = new(tableData, Endianness.Little);
        int count = reader.Read<int>();

        Dictionary<ulong, ulong> table = [];
        for (int i = 0; i < count; i++) {
            table[reader.Read<ulong>()] = reader.Read<ulong>();
        }

        return table.ToFrozenDictionary();
    }
}

public record TotkDumpResults(bool IsCompleteDump, List<string> BadFiles, List<string> ExtraFiles, int MissingFiles);
