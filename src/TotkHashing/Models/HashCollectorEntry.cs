using System.Text.Json.Serialization;

namespace TotkHashing.Models;

public struct HashCollectorEntry
{
    [JsonInclude]
    public int Version;

    [JsonInclude]
    public int Size;

    [JsonInclude]
    public ulong Hash;

    public readonly void Deconstruct(out int version, out int size, out ulong hash)
    {
        version = Version;
        size = Size;
        hash = Hash;
    }
}
