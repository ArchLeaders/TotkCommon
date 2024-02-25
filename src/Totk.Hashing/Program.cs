using Totk.Hashing;

if (args.Length < 2) {
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
if (output is "-d" or "--debug") {
    writeDebugFile = true;
    output = args[^2];
    inputs = args[..^2];
}

HashCollector collector = new(inputs);
await collector.Collect();

using FileStream fs = File.Create(output);
collector.Save(fs);

if (writeDebugFile) {
    using FileStream fsDebug = File.Create(Path.ChangeExtension(output, ".json"));
    collector.SaveDebug(fsDebug);
}
