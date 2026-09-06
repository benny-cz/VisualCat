using VisualCat.Core.Store;

// Test-only process boundary. The ready line means the real store protocol owns the path.
if (args.Length is < 1 or > 2) return 2;
IDisposable usage;
try
{
    usage = args.Length == 2 && args[1] == "read" ? SessionAccess.Read(args[0]) : SessionAccess.Write(args[0]);
}
catch (SessionInUseException)
{
    Console.WriteLine("blocked");
    return 0;
}
using var owned = usage;
Console.WriteLine("reserved");
Console.Out.Flush();
_ = Console.ReadLine();
return 0;
