namespace TotkCommon.Extensions;

public static class PathExtension
{
    public static ReadOnlySpan<char> GetRelativePathUnchecked(this ReadOnlySpan<char> relativeTo, ReadOnlySpan<char> path)
    {
        return path[(relativeTo.Length + relativeTo[^1] switch {
            '\\' or '/' => 0,
            _ => 1
        })..];
    }

    public static unsafe void NormalizeInline(this ReadOnlySpan<char> path)
    {
        Span<char> unsafeMutablePath;
        fixed (char* ptr = path) {
            unsafeMutablePath = new(ptr, path.Length);
        }

        for (int i = 0; i < path.Length; i++) {
            ref char @char = ref unsafeMutablePath[i];
            @char = @char switch {
                '\\' => '/',
                _ => @char
            };
        }
    }
}
