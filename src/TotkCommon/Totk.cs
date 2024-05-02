using System.Text.Json;
using System.Text.Json.Serialization;
using TotkCommon.Extensions;

namespace TotkCommon;

public class Totk
{
    private static readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "totk", "Config.json");

    public static Totk Config { get; }
    public static Zstd Zstd { get; }

    static Totk()
    {
        if (!File.Exists(_path))
        {
            Config = new();
        }

        Zstd = new();

        using FileStream fs = File.OpenRead(_path);
        Config = JsonSerializer.Deserialize(fs, TotkConfigSerializerContext.Default.Totk)
            ?? new();
    }

    private string _gamePath = string.Empty;
    public string GamePath {
        get => _gamePath;
        set {
            _gamePath = value;
            Zstd.LoadDictionaries(ZsDicPath);
            Version = GamePath.GetRomfsVersionOrDefault(100);
        }
    }

    [JsonIgnore]
    public string ZsDicPath => Path.Combine(GamePath, "Pack", "ZsDic.pack.zs");

    [JsonIgnore]
    public int Version { get; private set; } = 100;

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using FileStream fs = File.Create(_path);
        JsonSerializer.Serialize(fs, this, TotkConfigSerializerContext.Default.Totk);
    }
}

[JsonSerializable(typeof(Totk))]
public partial class TotkConfigSerializerContext : JsonSerializerContext
{

}