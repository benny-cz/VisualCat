// The Android framework's own promotion-failure exception types, by their exact names, so a
// desktop unit can inject states only a device can produce (IA-12b). Nothing here stands in
// for behaviour: ForegroundPromotion.Classify matches on the type's full name, and the name
// is the whole contract. The real types live in Mono.Android, which the cross-platform layer
// under test does not — and must not — reference.

namespace Android.App
{
    internal sealed class ForegroundServiceStartNotAllowedException()
        : InvalidOperationException("startForeground is not allowed from the app's current state.");

    internal sealed class MissingForegroundServiceTypeException()
        : InvalidOperationException("No foreground service type was declared.");

    internal sealed class InvalidForegroundServiceTypeException()
        : InvalidOperationException("The requested foreground service type is invalid.");
}

namespace Java.Lang
{
    internal sealed class SecurityException()
        : InvalidOperationException("The permission required for this foreground service type is not held.");

    internal sealed class IllegalArgumentException()
        : InvalidOperationException("The foreground service type is not a subset of the declared types.");

    internal sealed class IllegalStateException()
        : InvalidOperationException("Not allowed to start service Intent: app is in background.");
}
