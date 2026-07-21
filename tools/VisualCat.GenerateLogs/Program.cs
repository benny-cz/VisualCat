using System.Globalization;
using VisualCat.Core.Generation;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: VisualCat.GenerateLogs <output> [lines] [seed]");
    return 2;
}

var output = Path.GetFullPath(args[0]);
var lines = args.Length > 1 ? long.Parse(args[1], CultureInfo.InvariantCulture) : 1_000_000;
var seed = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 42;
await using var stream = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
await SyntheticLogGenerator.GenerateAsync(stream, new SyntheticLogOptions(lines, seed)).ConfigureAwait(false);
Console.WriteLine(output);
return 0;
