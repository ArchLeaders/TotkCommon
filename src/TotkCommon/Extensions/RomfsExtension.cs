using CommunityToolkit.HighPerformance.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace TotkCommon.Extensions;

[Flags]
public enum RomfsFileAttributes
{
    None = 0,
    HasZsExtension = 1,
    HasMcExtension = 2,
    IsProductFile = 4,
}

public static partial class RomfsExtension
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<char> ToCanonical(this string fileRelativeToRomfs)
    {
        return ToCanonical(fileRelativeToRomfs.AsSpan(), [], out _);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<char> ToCanonical(this ReadOnlySpan<char> fileRelativeToRomfs)
    {
        return ToCanonical(fileRelativeToRomfs, [], out _);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<char> ToCanonical(this string fileRelativeToRomfs, out RomfsFileAttributes attributes)
    {
        return ToCanonical(fileRelativeToRomfs, [], out attributes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<char> ToCanonical(this ReadOnlySpan<char> fileRelativeToRomfs, out RomfsFileAttributes attributes)
    {
        return ToCanonical(fileRelativeToRomfs, [], out attributes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<char> ToCanonical(this string file, ReadOnlySpan<char> romfs)
    {
        return ToCanonical(file.AsSpan(), romfs, out _);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<char> ToCanonical(this string file, ReadOnlySpan<char> romfs, out RomfsFileAttributes attributes)
    {
        return ToCanonical(file.AsSpan(), romfs, out attributes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<char> ToCanonical(this ReadOnlySpan<char> file, ReadOnlySpan<char> romfs)
    {
        return ToCanonical(file, romfs, out _);
    }

    public static unsafe ReadOnlySpan<char> ToCanonical(this ReadOnlySpan<char> file, ReadOnlySpan<char> romfs, out RomfsFileAttributes attributes)
    {
        if (file.Length < romfs.Length)
        {
            throw new ArgumentException($"""
                The provided {nameof(romfs)} path is longer than the input {nameof(file)}.
                """, nameof(romfs));
        }

        attributes = 0;

        int size = file.Length - romfs.Length - file[^3..] switch {
            ".zs" => (int)(attributes |= RomfsFileAttributes.HasZsExtension) + 2,
            ".mc" => (int)(attributes |= RomfsFileAttributes.HasMcExtension) + 1,
            _ => 0
        };

        // Make a copy to avoid
        // mutating the input string
        string result = file[romfs.Length..(romfs.Length + size)].ToString();

        Span<char> canonical;

        fixed (char* ptr = result)
        {
            canonical = new(ptr, size);
        }

        int state = 0;
        for (int i = 0; i < size; i++)
        {
            ref char @char = ref canonical[i];

            state = (@char, size - i) switch {
                ('.', > 8) => canonical[i..(i + 8)] switch {
                    ".Product" => ((int)(attributes |= RomfsFileAttributes.IsProductFile) * (size -= 4) * (i += 8)) + 1,
                    _ => state
                },
                _ => state
            };

            @char = state switch {
                0 => @char,
                _ => @char = canonical[i + 4]
            };

            @char = @char switch {
                '\\' => '/',
                _ => @char
            };
        }

        return canonical[0] switch {
            '/' => canonical[1..size],
            _ => canonical[..size]
        };
    }

    public static int GetRomfsVersion(this string romfs) => GetRomfsVersionOrDefault(romfs, out _);
    public static int GetRomfsVersion(this string romfs, out string nsoid)
    {
        string regionLangMaskPath = Path.Combine(romfs, "System", "RegionLangMask.txt");
        return File.Exists(regionLangMaskPath) switch {
            true => ParseRegionLangMask(regionLangMaskPath, out nsoid),
            false => throw new FileNotFoundException($"""
                A RegionLangMask file could not be found: '{regionLangMaskPath}'
                """),
        };
    }

    public static int GetRomfsVersionOrDefault(this string romfs, int @default = 100) => GetRomfsVersionOrDefault(romfs, out _, @default);
    public static int GetRomfsVersionOrDefault(this string romfs, out string? nsoid, int @default = 100)
    {
        nsoid = default;
        string regionLangMaskPath = Path.Combine(romfs, "System", "RegionLangMask.txt");
        return File.Exists(regionLangMaskPath) switch {
            true => ParseRegionLangMask(regionLangMaskPath, out nsoid),
            false => @default,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ParseRegionLangMask(string regionLangMaskPath, out string nsoid)
    {
        using FileStream fs = File.OpenRead(regionLangMaskPath);
        int size = Convert.ToInt32(fs.Length);
        using SpanOwner<byte> buffer = SpanOwner<byte>.Allocate(size);
        fs.Read(buffer.Span);
        int lastCaretReturnIndex = buffer.Span.LastIndexOf((byte)'\r');
        nsoid = Encoding.UTF8.GetString(buffer.Span[(lastCaretReturnIndex + 2)..]);
        return int.Parse(buffer.Span[(lastCaretReturnIndex - 3)..lastCaretReturnIndex]);
    }
}
