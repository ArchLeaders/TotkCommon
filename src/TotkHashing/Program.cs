using TotkCommon.Components;
using TotkCommon.Extensions;
using TotkHashing;

if (args.Length == 3 && File.Exists(args[0]) && Directory.Exists(args[1]) && File.Exists(args[2])) {
    TotkChecksums checksums = TotkChecksums.FromFile(args[2]);
    Console.WriteLine($"""
        {args[0].ToCanonical(args[1])}: {checksums.IsFileVanilla(args[0], args[1])}
        """);
    return;
}

if (args.Length < 2)
{
    Console.WriteLine("""
        Invalid arguments, expected a list of game paths and an output file.

        Example:
          /totk/1.1.0/romfs /totk/1.1.1/romfs /totk/1.1.2/romfs /totk/1.2.0/romfs /totk/1.2.1/romfs path/to/output.bin --debug

        Options:
          [--debug|-d] Write a debug json file
        """);
}

string output = args[^1];
string[] inputs = args[..^1];

bool writeDebugFile = false;
if (output is "-d" or "--debug")
{
    writeDebugFile = true;
    output = args[^2];
    inputs = args[..^2];
}

HashCollector collector = new(inputs);
await collector.Collect();

using FileStream fs = File.Create(output);
collector.Save(fs);

if (writeDebugFile)
{
    using FileStream fsDebug = File.Create(Path.ChangeExtension(output, ".json"));
    collector.SaveDebug(fsDebug);
}