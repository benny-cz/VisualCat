using Avalonia;
using Avalonia.Controls;

namespace VisualCat.App.Views;

/// <summary>
/// Something that can put a <see cref="DialogBody{TResult}"/> on screen and wait for it.
/// </summary>
/// <remarks>
/// The desktop shows a modal window; a phone has no windows at all, and every dialog the
/// application owned was a <see cref="Window"/> guarded by
/// <c>TopLevel.GetTopLevel(this) is not Window</c> — so on Android, Recent sessions,
/// Appearance, Session cache and the diagnostic bundle each returned immediately and did
/// nothing, which is exactly the "always enabled and silently does nothing" shape finding 19
/// is about. Presentation is the host's business; the dialog itself only builds a form and
/// says what it decided.
/// </remarks>
internal interface IDialogHost
{
    /// <summary>Presents the dialog and completes with its result, or with the default when
    /// it is dismissed.</summary>
    Task<TResult?> ShowDialogAsync<TResult>(DialogBody<TResult> body);
}

/// <summary>
/// A dialog's content and outcome, independent of how it is presented.
/// </summary>
public abstract class DialogBody<TResult> : UserControl
{
    private readonly TaskCompletionSource<TResult?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected DialogBody(string title) => DialogTitle = title;

    /// <summary>Window title on the desktop, header row when hosted in-page.</summary>
    public string DialogTitle { get; }

    /// <summary>Size the desktop window opens at. Ignored by an in-page host.</summary>
    public Size PreferredSize { get; protected init; } = new(520, 620);

    /// <summary>Smallest useful desktop window size.</summary>
    public Size MinimumSize { get; protected init; } = new(420, 320);

    /// <summary>
    /// Whether this body already scrolls its own content and must not be put inside a second
    /// scroller.
    /// </summary>
    /// <remarks>
    /// The in-page host wraps a dialog in a <see cref="ScrollViewer"/>, which is right for a
    /// short form and wrong for one that ends in a decision. With nine cached sessions, the
    /// whole of <c>Recent sessions</c> scrolled — list <em>and</em> its Cancel/Open row — so
    /// the button that confirms the tapped session sat two screens below it and the first tap
    /// read as a dead control (finding 16). A body that says it scrolls internally is given
    /// the sheet's height directly and keeps its own footer where it put it.
    /// </remarks>
    internal bool ScrollsInternally { get; init; }

    /// <summary>Completes with the dialog's result, or the default when it is dismissed.</summary>
    public Task<TResult?> Completion => _completion.Task;

    /// <summary>The host, for a dialog that needs to present another one.</summary>
    internal IDialogHost? Host { get; set; }

    /// <summary>Records the dialog's outcome. The host takes it down.</summary>
    protected void Complete(TResult? result) => _completion.TrySetResult(result);

    /// <summary>Dismissal: closing a window, tapping the scrim, or the system Back gesture.</summary>
    internal void Dismiss() => _completion.TrySetResult(default);

    /// <summary>Runs once the body is on screen, for work that needs a live visual tree.</summary>
    protected virtual void OnPresented()
    {
    }

    internal void NotifyPresented() => OnPresented();

    /// <summary>Presents another dialog on the same host — a confirmation, typically.</summary>
    protected Task<TNested?> ShowNestedAsync<TNested>(DialogBody<TNested> body) =>
        Host is { } host ? host.ShowDialogAsync(body) : Task.FromResult<TNested?>(default);
}
