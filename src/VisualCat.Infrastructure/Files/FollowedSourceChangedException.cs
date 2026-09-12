namespace VisualCat.Infrastructure.Files;

/// <summary>
/// The file a follow was reading stopped being the file at the path it was following.
/// </summary>
/// <remarks>
/// Its own type because its message is already written for a person and says what happened,
/// what the configured policy did about it, and what was kept. Raised as a plain
/// <see cref="IOException"/>, it was wrapped for display — "VisualCat could not read or write
/// the session: The followed file was replaced (rotated); …" — which prefixes a complete
/// sentence with a vaguer one that is not what happened (findings F-31, A-21).
/// </remarks>
public sealed class FollowedSourceChangedException(string message) : IOException(message);
