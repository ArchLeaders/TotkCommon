namespace Totk.Hashing.Models;

public class HashCollectorEntry
{
    public int Size { get; set; }
    public ulong Hash { get; set; }
    public Dictionary<int, HashCollectorEntry> Versions { get; set; } = [];

    public override bool Equals(object? obj)
    {
        return obj is HashCollectorEntry entry ? entry.Size == Size && entry.Hash == Hash : base.Equals(obj);
    }

    public override int GetHashCode()
    {
        return Size.GetHashCode() + Hash.GetHashCode();
    }
}
