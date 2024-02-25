namespace Totk.Common.Extensions;

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
    public static string ToCanonical(this string file, string romfs)
    {
        return file.ToCanonical(romfs, out _);
    }

    public static string ToCanonical(this string file, string romfs, out RomfsFileAttributes attributes)
    {
        return Path.GetRelativePath(romfs, file).ToCanonical(out attributes);
    }

    public static string ToCanonical(this string fileRelativeToRomfs)
    {
        return fileRelativeToRomfs.ToCanonical(out _);
    }

    public static string ToCanonical(this string fileRelativeToRomfs, out RomfsFileAttributes attributes)
    {
        attributes = RomfsFileAttributes.None;
        ReadOnlySpan<char> path = fileRelativeToRomfs;
        ReadOnlySpan<char> ext = Path.GetExtension(path);

        if (ext.Length != 3) {
            return ToCanonicalInternal(path, ref attributes);
        }

        if (ext is ".zs") {
            attributes |= RomfsFileAttributes.HasZsExtension;
            ext = Path.GetExtension(path = path[..^3]);
        }

        if (ext is ".mc") {
            attributes |= RomfsFileAttributes.HasMcExtension;
            path = path[..^3];
        }

        return ToCanonicalInternal(path, ref attributes);
    }

    public static int GetRomfsVersionOrDefault(this string romfs, int @default = 100)
    {
        string regionLangMask = Path.Combine(romfs, "System", "RegionLangMask.txt");
        if (File.Exists(regionLangMask)) {
            string[] lines = File.ReadAllLines(regionLangMask);
            if (lines.Length >= 3 && int.TryParse(lines[2], out int value)) {
                return value;
            }
        }

        return @default;
    }

    private static string ToCanonicalInternal(ReadOnlySpan<char> path, ref RomfsFileAttributes attributes)
    {
        Span<char> result = path.Length > 0x1000
            ? new char[path.Length] : stackalloc char[path.Length];
        path.Replace(result, '\\', '/');

        ReadOnlySpan<char> name = Path.GetFileName(path);
        int firstExtensionDelimiter = name.IndexOf('.');
        int supposedNextDelimiter = firstExtensionDelimiter + 8;
        if (name.Length > supposedNextDelimiter && name[supposedNextDelimiter] is '.' && name[++firstExtensionDelimiter..supposedNextDelimiter] is "Product") {
            attributes |= RomfsFileAttributes.IsProductFile;
            supposedNextDelimiter += 4;
            while (name.Length > ++supposedNextDelimiter) {
                result[^(name.Length - supposedNextDelimiter + 4)] = name[supposedNextDelimiter];
            }

            result = result[..^4];
        }

        return new string(result);
    }
}
