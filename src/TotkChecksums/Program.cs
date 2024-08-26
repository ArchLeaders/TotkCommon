// 
// Generates checksum tables used to verify the integrity of a TotK game dump

using System.Diagnostics;
using TotkChecksums;

Stopwatch stopwatch = Stopwatch.StartNew();
ChecksumTable table = ChecksumTable.FromGameDump(args[0]);

string output = Path.GetFileName(args[0]);
using FileStream fs = File.Create(output);
table.WriteBinary(fs);

await Console.Out.WriteLineAsync($"""

    Completed '{args[0]}' in {stopwatch.ElapsedMilliseconds / 1000.0 / 60.0} minutes.
    """);
