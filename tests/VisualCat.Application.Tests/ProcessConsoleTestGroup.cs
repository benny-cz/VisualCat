namespace VisualCat.Application.Tests;

/// <summary>
/// Process-wide console streams cannot be owned by two tests at once. Keep every test that
/// replaces one in this collection so diagnostics cannot leak into a neighbouring assertion.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessConsoleTestGroup
{
    public const string Name = "Process console";
}
