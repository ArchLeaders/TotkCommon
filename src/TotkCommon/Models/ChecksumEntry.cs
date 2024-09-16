#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value

namespace TotkCommon.Models;

internal struct ChecksumEntry
{
    public int Version;
    public int Size;
    public ulong Hash;
}
