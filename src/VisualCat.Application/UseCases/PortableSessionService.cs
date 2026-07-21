using VisualCat.Core.Store;

namespace VisualCat.Application.UseCases;

public static class PortableSessionService
{
    public static Task SavePortableAsync(
        SessionSnapshot snapshot,
        string destination,
        CancellationToken cancellationToken = default) =>
        SessionSaveService.SaveAsync(snapshot, destination, portable: true, cancellationToken);
}
