namespace VisualCat.Application.Ports;

public sealed record DiagnosticEvent(
    DateTimeOffset TimestampUtc,
    string Level,
    string Subsystem,
    string Name,
    Guid? SessionId,
    long? CoordinatorGeneration,
    IReadOnlyDictionary<string, string> Properties);

public interface IDiagnosticSink : IAsyncDisposable
{
    ValueTask WriteAsync(DiagnosticEvent diagnosticEvent, CancellationToken cancellationToken = default);
}
