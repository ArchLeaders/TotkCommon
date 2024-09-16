using BymlLibrary;
using BymlLibrary.Extensions;
using CommunityToolkit.HighPerformance.Buffers;
using Revrs;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using TotkCommon.Extensions;

namespace TotkCommon;

// ReSharper disable UnusedMember.Global

public class Totk
{
    private static readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "totk", "Config.json");
    
    public const string TITLE_ID = "0100F2C0115B6000";

    public static Totk Config { get; }
    public static Zstd Zstd { get; }
    public static FrozenDictionary<string, string>? AddressTable { get; private set; }

    static Totk()
    {
        Zstd = new Zstd();

        if (!File.Exists(_path)) {
            Config = new Totk();
            return;
        }

        using FileStream fs = File.OpenRead(_path);
        Config = JsonSerializer.Deserialize(fs, TotkConfigSerializerContext.Default.Totk)
            ?? new Totk();
    }

    private string _gamePath = string.Empty;
    public string GamePath {
        get => _gamePath;
        set {
            _gamePath = value;
            Version = GamePath.GetRomfsVersionOrDefault(out string? nsoid, 100);
            if (nsoid is not null) {
                NSOBID = nsoid;
            }

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

    [JsonIgnore]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public string NSOBID { get; private set; } = "082CE09B06E33A123CB1E2770F5F9147709033DB";

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
        _ = fs.Read(buffer.Span);

        size = Zstd.GetDecompressedSize(buffer.Span);
        using SpanOwner<byte> decompressed = SpanOwner<byte>.Allocate(size);
        Zstd.Decompress(buffer.Span, decompressed.Span);

        Dictionary<string, string> addressTable = [];

        RevrsReader reader = new(decompressed.Span);
        ImmutableByml root = new(ref reader);
        foreach ((int keyIndex, ImmutableByml value) in root.GetMap()) {
            addressTable[root.KeyTable[keyIndex].ToManaged()] = root.StringTable[value.GetStringIndex()].ToManaged();
        }

        AddressTable = addressTable.ToFrozenDictionary();
    }
}

[JsonSerializable(typeof(Totk))]
public partial class TotkConfigSerializerContext : JsonSerializerContext;