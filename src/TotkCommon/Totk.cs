using BymlLibrary;
using BymlLibrary.Extensions;
using CommunityToolkit.HighPerformance.Buffers;
using Revrs;
using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using TotkCommon.Extensions;

namespace TotkCommon;

public class Totk
{
    private static readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "totk", "Config.json");

    public static Totk Config { get; }
    public static Zstd Zstd { get; }
    public static FrozenDictionary<string, string>? AddressTable { get; private set; }

    static Totk()
    {
        Zstd = new();

        if (!File.Exists(_path)) {
            Config = new();
            return;
        }

        using FileStream fs = File.OpenRead(_path);
        Config = JsonSerializer.Deserialize(fs, TotkConfigSerializerContext.Default.Totk)
            ?? new();
    }

    private string _gamePath = string.Empty;
    public string GamePath {
        get => _gamePath;
        set {
            _gamePath = value;
            Version = GamePath.GetRomfsVersionOrDefault(100);

            if (File.Exists(ZsDicPath)) {
                Zstd.LoadDictionaries(ZsDicPath);
            }

            if (File.Exists(AddressTablePath)) {
                BuildAddressTable();
            }
        }
    }

    [JsonIgnore]
    public string ZsDicPath => Path.Combine(GamePath, "Pack", "ZsDic.pack.zs");

    [JsonIgnore]
    public string AddressTablePath => Path.Combine(GamePath, "System", "AddressTable", $"Product.{Version}.Nin_NX_NVN.atbl.byml.zs");

    [JsonIgnore]
    public int Version { get; private set; } = 100;

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using FileStream fs = File.Create(_path);
        JsonSerializer.Serialize(fs, this, TotkConfigSerializerContext.Default.Totk);
    }

    private void BuildAddressTable()
    {
        using FileStream fs = File.OpenRead(AddressTablePath);
        int size = Convert.ToInt32(fs.Length);
        using SpanOwner<byte> buffer = SpanOwner<byte>.Allocate(size);
        fs.Read(buffer.Span);

        size = Zstd.GetDecompressedSize(buffer.Span);
        using SpanOwner<byte> decompressed = SpanOwner<byte>.Allocate(size);
        Zstd.Decompress(buffer.Span, decompressed.Span);

        Dictionary<string, string> addressTable = [];

        RevrsReader reader = new(decompressed.Span);
        ImmutableByml root = new(ref reader);
        foreach (var (keyIndex, value) in root.GetMap()) {
            addressTable[root.KeyTable[keyIndex].ToManaged()] = root.StringTable[value.GetStringIndex()].ToManaged();
        }

        AddressTable = addressTable.ToFrozenDictionary();
    }
}

[JsonSerializable(typeof(Totk))]
public partial class TotkConfigSerializerContext : JsonSerializerContext
{

}