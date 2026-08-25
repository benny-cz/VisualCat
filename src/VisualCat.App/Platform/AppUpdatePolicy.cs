using System.Globalization;
using VisualCat.Domain;

namespace VisualCat.App.Platform;

/// <summary>What, if anything, to put in front of the reader about an update.</summary>
public enum AppUpdatePromptAction
{
    /// <summary>The message is a report. It gets no button.</summary>
    None,

    /// <summary>Ask the store to download in the background, leaving the app running.</summary>
    StartFlexible,

    /// <summary>Hand the screen to the store, which installs and restarts the app.</summary>
    StartImmediate,

    /// <summary>Install what the store has already downloaded. This restarts the app.</summary>
    CompleteInstall,

    /// <summary>Open this app's page in the store, for a flow the API could not start.</summary>
    OpenStore,

    /// <summary>Open the project's releases page, for a build no store can update.</summary>
    OpenReleases,
}

/// <summary>One message about updates, and the single thing the reader can do about it.</summary>
/// <param name="Message">Exactly what goes in the notice lane.</param>
/// <param name="ActionLabel">The verb on the button, or null for a report with no button.</param>
/// <param name="Action">What that button does.</param>
/// <param name="Persistent">Whether the lane holds it until the reader dismisses it.</param>
public sealed record AppUpdatePrompt(
    string Message,
    string? ActionLabel,
    AppUpdatePromptAction Action,
    bool Persistent);

/// <summary>Everything the app remembers about update prompts between launches.</summary>
/// <param name="DismissedVersionCode">The version code the reader has already said no to.</param>
/// <param name="SnoozedUntilUtc">When the reader may next be asked.</param>
/// <param name="LastCheckedUtc">When the store was last asked.</param>
public sealed record AppUpdateMemory(
    long DismissedVersionCode = 0,
    DateTimeOffset? SnoozedUntilUtc = null,
    DateTimeOffset? LastCheckedUtc = null)
{
    /// <summary>Nothing has been asked or dismissed yet.</summary>
    public static AppUpdateMemory Empty { get; } = new();
}

/// <summary>
/// When to ask the store about an update, and what to say about the answer.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately free of Avalonia, of Android and of a clock of its own: everything it needs
/// arrives as a parameter. That is what makes the channel throttles, the snooze arithmetic
/// and — the rule that matters — the live-capture guard testable without a device, which is
/// the only way any of them can be trusted, since the real path cannot be exercised outside a
/// Play install.
/// </para>
/// <para>
/// The product this guards is a log analyser that people run mid-investigation. An update
/// prompt that interrupts a recording is a worse defect than being one version behind, and an
/// update that <em>installs</em> during a recording destroys it: both Play flows kill and
/// restart the process, which ends a capture without the drain-seal-reopen that the in-app
/// Stop performs.
/// </para>
/// </remarks>
public static class AppUpdatePolicy
{
    /// <summary>
    /// Where a build that cannot update itself sends the reader instead.
    /// </summary>
    public const string ReleasesUrl = "https://github.com/benny-cz/VisualCat/releases";

    /// <summary>How long a cold start's answer stays good enough not to ask again.</summary>
    private static TimeSpan ResumeInterval(ReleaseChannel channel) => channel switch
    {
        // A closed tester who is not running the newest build is not testing anything, so
        // this is short enough to catch a build uploaded during the same session.
        ReleaseChannel.Alpha => TimeSpan.FromMinutes(15),
        ReleaseChannel.Beta => TimeSpan.FromHours(6),
        _ => TimeSpan.FromHours(24),
    };

    /// <summary>How long Dismiss buys before the same offer may be raised again.</summary>
    private static TimeSpan SnoozeFor(ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Alpha => TimeSpan.FromHours(4),
        ReleaseChannel.Beta => TimeSpan.FromHours(24),
        _ => TimeSpan.FromDays(7),
    };

    /// <summary>The priority at or above which a dismissal is overridden.</summary>
    private static int InsistentPriority(ReleaseChannel channel) =>
        channel == ReleaseChannel.Alpha ? 3 : 4;

    /// <summary>The priority at or above which the store may take the screen.</summary>
    private static int ImmediatePriority(ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Alpha or ReleaseChannel.Beta => 4,
        _ => 5,
    };

    /// <summary>Whether this build ever prompts about updates on its own.</summary>
    /// <remarks>
    /// A development build does not: it is a developer or CI artifact, an untagged workflow
    /// run versions itself <c>-preview.N</c>, and neither has a reader who wants to be told
    /// about a Play release. A manual check still answers, because the reader asked.
    /// </remarks>
    public static bool PromptsAutomatically(ReleaseChannel channel, AppInstallOrigin origin) =>
        channel != ReleaseChannel.Development && origin == AppInstallOrigin.PlayStore;

    /// <summary>Whether it is worth asking the store at all right now.</summary>
    /// <param name="channel">Which audience this build was made for.</param>
    /// <param name="origin">Where this installation came from.</param>
    /// <param name="coldStart">True for the check at startup, false for one on resume.</param>
    /// <param name="memory">What the app remembers from previous launches.</param>
    /// <param name="nowUtc">The current time, passed in so this stays testable.</param>
    public static bool ShouldCheck(
        ReleaseChannel channel,
        AppInstallOrigin origin,
        bool coldStart,
        AppUpdateMemory memory,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(memory);
        if (!PromptsAutomatically(channel, origin))
        {
            return false;
        }

        var since = memory.LastCheckedUtc is { } last ? nowUtc - last : TimeSpan.MaxValue;

        // A clock that moved backwards — a timezone edit, a restored backup — must not be able
        // to suppress checking for however long it went back.
        if (since < TimeSpan.Zero)
        {
            return true;
        }

        if (coldStart)
        {
            // Testers are here to run the newest build; a stable user is not, and asking Play
            // on every launch is an IPC round trip for an answer that rarely moves.
            return channel is ReleaseChannel.Alpha or ReleaseChannel.Beta || since >= TimeSpan.FromHours(24);
        }

        return since >= ResumeInterval(channel);
    }

    /// <summary>
    /// What to say about the store's answer, if anything.
    /// </summary>
    /// <param name="status">What the store reported.</param>
    /// <param name="channel">Which audience this build was made for.</param>
    /// <param name="liveCaptureRunning">Whether a recording is in progress right now.</param>
    /// <param name="memory">What the app remembers from previous launches.</param>
    /// <param name="nowUtc">The current time, passed in so this stays testable.</param>
    /// <param name="manual">True when the reader asked; a question typed always gets an answer.</param>
    /// <param name="origin">
    /// Where this installation came from. Used only to say why a build cannot update itself,
    /// which is a different sentence for a file and for another store.
    /// </param>
    /// <returns>The message to raise, or null to say nothing at all.</returns>
    public static AppUpdatePrompt? Decide(
        AppUpdateStatus status,
        ReleaseChannel channel,
        bool liveCaptureRunning,
        AppUpdateMemory memory,
        DateTimeOffset nowUtc,
        bool manual = false,
        AppInstallOrigin origin = AppInstallOrigin.PlayStore)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(memory);

        // Work already in flight is reported whatever the channel says, because the app
        // started it and the reader is waiting on it. Everything below this is an offer.
        var name = Name(status);
        var subject = Sentence(name);
        switch (status.State)
        {
            case AppUpdateState.Downloading:
                return new AppUpdatePrompt(
                    Downloading(name, status),
                    ActionLabel: null,
                    AppUpdatePromptAction.None,
                    Persistent: false);

            case AppUpdateState.ReadyToInstall when liveCaptureRunning:
                // Rule 2 of the live-capture guard: CompleteUpdate restarts the process, and
                // the recording would end without the drain-seal-reopen the in-app Stop does.
                // The install is not refused, it is deferred — and the wording says whose
                // move it is, because the reader can end the capture and this must not.
                return new AppUpdatePrompt(
                    $"{subject} is downloaded. Stop the capture to install it — installing restarts the app.",
                    ActionLabel: null,
                    AppUpdatePromptAction.None,
                    Persistent: true);

            case AppUpdateState.ReadyToInstall:
                return new AppUpdatePrompt(
                    $"{subject} is downloaded. Installing restarts the app.",
                    "Install",
                    AppUpdatePromptAction.CompleteInstall,
                    Persistent: true);

            case AppUpdateState.InProgress when liveCaptureRunning:
                return new AppUpdatePrompt(
                    $"An update to {name} was interrupted. Stop the capture to resume it — installing restarts the app.",
                    ActionLabel: null,
                    AppUpdatePromptAction.None,
                    Persistent: true);

            case AppUpdateState.InProgress:
                return new AppUpdatePrompt(
                    $"An update was interrupted. Resume it to finish installing {name}.",
                    "Resume",
                    AppUpdatePromptAction.StartImmediate,
                    Persistent: true);

            case AppUpdateState.Failed:
                return new AppUpdatePrompt(
                    status.Message ?? "The update did not start. Try again from the Play Store.",
                    "Open Play",
                    AppUpdatePromptAction.OpenStore,
                    Persistent: true);
        }

        if (!manual && channel == ReleaseChannel.Development)
        {
            // A development build never raises an offer by itself. It still answers a manual
            // check, which is a question the reader typed.
            return null;
        }

        switch (status.State)
        {
            case AppUpdateState.Unsupported:
                // Two different facts, and saying the wrong one is the same class of defect as
                // a notice naming a command the reader cannot run: an app installed by another
                // store was not "installed from a file", and telling somebody it was sends them
                // looking for a file they never had.
                return manual
                    ? new AppUpdatePrompt(
                        origin == AppInstallOrigin.OtherStore
                            ? "This build was installed by another store, so Google Play cannot update it. Releases are also published on GitHub."
                            : "This build was installed from a file, so Google Play cannot update it. Releases are published on GitHub.",
                        "Open releases",
                        AppUpdatePromptAction.OpenReleases,
                        Persistent: true)
                    : null;

            case AppUpdateState.UpToDate:
                // Only for a manual check. A background check that finds nothing says nothing.
                return manual
                    ? new AppUpdatePrompt(
                        $"{Running()} is the newest build Google Play has for you.",
                        ActionLabel: null,
                        AppUpdatePromptAction.None,
                        Persistent: false)
                    : null;

            case AppUpdateState.Unknown:
                // Never "you are up to date". An offline device that says it is current is
                // giving confident wrong news about the one thing it could not find out.
                return manual
                    ? new AppUpdatePrompt(
                        "Google Play could not be reached, so there is nothing to report about updates.",
                        ActionLabel: null,
                        AppUpdatePromptAction.None,
                        Persistent: false)
                    : null;

            case AppUpdateState.Available:
                break;

            default:
                return null;
        }

        // The live-capture guard, in its strongest form: an offer the reader did not ask for
        // is not raised over a running recording at all. A banner arriving on top of a capture
        // somebody just started reads as an error about the capture, and this lane is where
        // capture failures are reported. The offer is not lost — the view re-decides when the
        // recording ends, and Play will still have it.
        if (!manual && liveCaptureRunning)
        {
            return null;
        }

        if (!manual && !MayOffer(status, channel, memory, nowUtc))
        {
            return null;
        }

        // Rule 1 of the live-capture guard: the store's take-the-screen flow installs and
        // restarts the process, which ends a recording without the drain-seal-reopen that the
        // in-app Stop performs. A download during a capture is harmless — only the install
        // touches the process — so a manual check mid-capture offers exactly that much.
        var immediate = status.ImmediateAllowed &&
                        status.Priority >= ImmediatePriority(channel) &&
                        !liveCaptureRunning;
        var action = immediate ? AppUpdatePromptAction.StartImmediate : AppUpdatePromptAction.StartFlexible;
        if (!immediate && !status.FlexibleAllowed)
        {
            // Play knows about a newer build but will not let this app start either flow —
            // an unmetered-only preference, a device policy. Sending the reader to the store
            // is the honest remaining move; offering an Update button that cannot work is not.
            return new AppUpdatePrompt(
                $"{subject} is available, but Google Play cannot start the update from here.",
                "Open Play",
                AppUpdatePromptAction.OpenStore,
                Persistent: true);
        }

        if (liveCaptureRunning)
        {
            // Reached only by a manual check. Naming the download as the whole of what this
            // button does is what keeps the reader's capture safe from their own tap.
            return new AppUpdatePrompt(
                $"{subject} is available. Downloading it now is safe; installing restarts the app, so it waits until the capture stops.",
                "Download",
                AppUpdatePromptAction.StartFlexible,
                Persistent: true);
        }

        var channelNote = channel switch
        {
            // What the app can state honestly is its own channel. It cannot learn which track
            // the offered build came from — Play reports a version code and nothing else — so
            // this says where the reader is, never where the update is.
            ReleaseChannel.Alpha => " You are on the alpha channel.",
            ReleaseChannel.Beta => " You are on the beta channel.",
            _ => string.Empty,
        };

        return new AppUpdatePrompt(
            $"{subject} is available on Google Play.{channelNote}",
            "Update",
            action,
            Persistent: true);
    }

    /// <summary>
    /// Whether an offer for this version code may be raised without the reader having asked.
    /// </summary>
    private static bool MayOffer(
        AppUpdateStatus status,
        ReleaseChannel channel,
        AppUpdateMemory memory,
        DateTimeOffset nowUtc)
    {
        // A build newer than the one the reader refused is a different question, so the answer
        // they gave to the old one does not carry. This is also what stops a dismissal from
        // outliving the release it was about.
        if (status.AvailableVersionCode > memory.DismissedVersionCode)
        {
            return true;
        }

        // The same build they already declined. Dismiss means "not now", not "never": the
        // channel's snooze runs out and the offer may be made again.
        if (memory.SnoozedUntilUtc is not { } until || nowUtc >= until)
        {
            return true;
        }

        // Inside the snooze, only urgency reopens it — a priority the release was published
        // with, or, on stable alone, a build this user has been eligible for long enough that
        // "later" has stopped meaning anything.
        if (status.Priority >= InsistentPriority(channel))
        {
            return true;
        }

        // Null staleness means Play did not say, which is not the same as zero. Treating it as
        // zero would make an unknown look like a fresh release and suppress the escalation.
        return channel == ReleaseChannel.Stable && status.StalenessDays is >= 30;
    }

    /// <summary>How long Dismiss buys, from the moment it was pressed.</summary>
    public static DateTimeOffset SnoozeUntil(ReleaseChannel channel, DateTimeOffset nowUtc) =>
        nowUtc + SnoozeFor(channel);

    /// <summary>
    /// What to call the offered build in a sentence.
    /// </summary>
    /// <remarks>
    /// Never "a new version" when the version is known, and never a made-up version when it is
    /// not: a code that does not decode produces "a newer VisualCat", which is vague but true.
    /// </remarks>
    private static string Name(AppUpdateStatus status) =>
        status.AvailableVersionName is { Length: > 0 } version
            ? $"VisualCat {version}"
            : "a newer VisualCat";

    /// <summary>
    /// The same name, at the start of a sentence.
    /// </summary>
    /// <remarks>
    /// The unnamed fallback is an article rather than a proper noun, so it reads as "a newer
    /// VisualCat" mid-sentence and has to be "A newer VisualCat" when it opens one. Two spellings
    /// of the same fact rather than a message that avoids ever starting with it, because
    /// "A newer VisualCat is downloaded" is the sentence a reader wants first.
    /// </remarks>
    private static string Sentence(string name) =>
        name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    private static string Running() => $"VisualCat {ProductInfo.DisplayVersion}";

    private static string Downloading(string name, AppUpdateStatus status)
    {
        var subject = Sentence(name);
        return status.TotalBytesToDownload > 0
            ? $"Downloading {name} · {Megabytes(status.BytesDownloaded)} of {Megabytes(status.TotalBytesToDownload)}."
            : $"{subject} is downloading in the background.";
    }

    private static string Megabytes(long bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.0} MB");
}
