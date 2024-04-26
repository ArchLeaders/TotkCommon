using System.Text.Json;
using System.Text.Json.Serialization;

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

        using FileStream fs = File.OpenRead(_path);
        Config = JsonSerializer.Deserialize<Totk>(fs)
            ?? new();

        Zstd = new();
        Zstd.LoadDictionaries(Config.ZsDicPath);
    }

    private string _gamePath = string.Empty;
    public string GamePath
    {
        get => _gamePath;
        set
        {
            _gamePath = value;
            Zstd.LoadDictionaries(ZsDicPath);
        }
    }

    [JsonIgnore]
    public string ZsDicPath => Path.Combine(GamePath, "Pack", "ZsDic.pack.zs");

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using FileStream fs = File.Create(_path);
        JsonSerializer.Serialize(fs, this);
    }
}

