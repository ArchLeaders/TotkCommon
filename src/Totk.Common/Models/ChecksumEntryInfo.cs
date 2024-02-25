using System.Runtime.InteropServices;

namespace Totk.Common.Models;

[StructLayout(LayoutKind.Explicit, Pack = 4, Size = 4)]
internal struct ChecksumEntryInfo
{
    [FieldOffset(0)]
    public int FirstVersionIndex;

    [FieldOffset(0)]
    public int Version;
}
