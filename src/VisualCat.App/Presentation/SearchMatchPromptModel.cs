using System.ComponentModel;
using System.Globalization;
using VisualCat.Domain.Queries;

namespace VisualCat.App.Presentation;

/// <summary>What the reader chose in <em>Go to match</em>, and the result it described.</summary>
/// <param name="Ordinal">The one-based match number, already validated against <paramref name="Identity"/>.</param>
/// <param name="Identity">The applied query the prompt was showing when the reader confirmed.</param>
/// <param name="Key">The exact record resolved in that displayed query, when the host supports it.</param>
public sealed record SearchMatchPromptSelection(
    long Ordinal,
    QueryIdentity Identity,
    SearchMatchKey? Key = null);

/// <summary>
/// The live state a <em>Go to match</em> prompt reads while it is open.
/// </summary>
/// <remarks>
/// <para>
/// The prompt used to be a fixed numeric range handed over once. That is wrong for a capture
/// that is still growing: a prompt opened at "1 to 7,181" was still saying so after the
/// search had found 12,000 more matches, and it could apply a number to a search the reader
/// had since replaced. The number the reader types is only meaningful together with the
/// applied query it was counted against, so the prompt observes that query and reports both.
/// </para>
/// <para>
/// A total that falls to zero must not become the contradictory numeric range "1 to 0": the
/// prompt keeps the reader's typed text, says there is nothing to go to, and comes back when
/// a later result has matches again.
/// </para>
/// </remarks>
public sealed class SearchMatchPromptModel : INotifyPropertyChanged
{
    private QueryIdentity? _identity;
    private long _maximum;
    private bool _isPending;
    private bool _isResolving;
    private string? _resolutionError;
    private readonly Func<SearchMatchPromptSelection, CancellationToken, Task<SearchMatchKey?>>? _resolveAsync;

    /// <param name="identity">The applied query whose matches are being numbered.</param>
    /// <param name="maximum">How many matches that query found.</param>
    /// <param name="resolveAsync">Optionally resolves the accepted ordinal to its exact key.</param>
    public SearchMatchPromptModel(
        QueryIdentity identity,
        long maximum,
        Func<SearchMatchPromptSelection, CancellationToken, Task<SearchMatchKey?>>? resolveAsync = null)
    {
        _identity = identity;
        _maximum = Math.Max(0, maximum);
        _resolveAsync = resolveAsync;
        OpenedFilterFingerprint = identity.FilterFingerprint;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the prompt can no longer answer the question it is asking.</summary>
    public event Action<string>? CloseRequested;

    /// <summary>The filter the prompt was opened against; a different one closes it.</summary>
    public string OpenedFilterFingerprint { get; }

    /// <summary>The applied query the displayed total belongs to.</summary>
    public QueryIdentity? AppliedIdentity
    {
        get => _identity;
        private set => Set(ref _identity, value, nameof(AppliedIdentity));
    }

    /// <summary>How many matches the displayed applied query found.</summary>
    public long Maximum
    {
        get => _maximum;
        private set
        {
            if (Set(ref _maximum, value, nameof(Maximum)))
            {
                Raise(nameof(HasMatches));
                Raise(nameof(RangeText));
                Raise(nameof(CanConfirm));
            }
        }
    }

    /// <summary>Whether a newer result is still being applied.</summary>
    public bool IsPending
    {
        get => _isPending;
        private set
        {
            if (Set(ref _isPending, value, nameof(IsPending)))
            {
                Raise(nameof(CanConfirm));
            }
        }
    }

    /// <summary>Whether a confirmation is already on its way to the workspace.</summary>
    public bool IsResolving
    {
        get => _isResolving;
        private set
        {
            if (Set(ref _isResolving, value, nameof(IsResolving)))
            {
                Raise(nameof(CanConfirm));
            }
        }
    }

    /// <summary>The reader's confirmed choice, once there is one.</summary>
    public SearchMatchPromptSelection? Selection { get; private set; }

    public bool HasMatches => Maximum > 0;

    /// <summary>Whether <em>Go</em> can act on what is currently displayed.</summary>
    public bool CanConfirm => HasMatches && !IsPending && !IsResolving && AppliedIdentity is not null;

    /// <summary>The sentence beneath the field: the range, or why there is none.</summary>
    public string RangeText => HasMatches
        ? $"1 to {Maximum.ToString("N0", CultureInfo.CurrentCulture)} · Search order: time"
        : "No matches in the current capture.";

    /// <summary>The message shown when the typed text is not a match number.</summary>
    public string ValidationMessage => _resolutionError ?? (HasMatches
        ? $"Enter a match number from 1 to {Maximum.ToString("N0", CultureInfo.CurrentCulture)}."
        : "No matches in the current capture.");

    /// <summary>
    /// Publishes a newly applied result. A different filter closes the prompt; a newer
    /// snapshot of the same search only refreshes the total the reader is choosing within.
    /// </summary>
    public void Apply(QueryIdentity? identity, long maximum, bool pending)
    {
        if (identity is { } applied &&
            !string.Equals(applied.FilterFingerprint, OpenedFilterFingerprint, StringComparison.Ordinal))
        {
            CloseRequested?.Invoke("Search changed. Open Go to match again.");
            return;
        }

        IsPending = pending;
        if (identity is null)
        {
            return;
        }

        AppliedIdentity = identity;
        Maximum = maximum;
    }

    /// <summary>Reads the typed text as a match number, without clamping or rounding it.</summary>
    /// <remarks>
    /// The spin control's own <c>Value</c> has already clamped an overflowing entry and
    /// rounded a fractional one by the time it can be read, so trusting it would silently
    /// turn "0.5" and "999999999999" into different, valid match numbers the reader never
    /// asked for.
    /// </remarks>
    public bool TryReadOrdinal(string? text, out long ordinal)
    {
        ordinal = 0;
        if (!HasMatches || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var candidate = text.Trim();
        if (!long.TryParse(
                candidate,
                NumberStyles.AllowThousands | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                CultureInfo.CurrentCulture,
                out var value) &&
            !long.TryParse(
                candidate,
                NumberStyles.AllowThousands | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                CultureInfo.InvariantCulture,
                out value))
        {
            return false;
        }

        if (value < 1 || value > Maximum)
        {
            return false;
        }

        ordinal = value;
        return true;
    }

    /// <summary>Records the confirmed choice with the identity it was counted against.</summary>
    public bool TryConfirm(string? text, out long ordinal)
    {
        ordinal = 0;
        if (!CanConfirm || AppliedIdentity is not { } identity || !TryReadOrdinal(text, out ordinal))
        {
            return false;
        }

        IsResolving = true;
        SetResolutionError(null);
        Selection = new SearchMatchPromptSelection(ordinal, identity);
        return true;
    }

    /// <summary>
    /// Resolves the accepted ordinal against the exact displayed query before the dialog
    /// releases it to a live workspace.
    /// </summary>
    public async Task<bool> ResolveSelectionAsync(CancellationToken cancellationToken = default)
    {
        if (Selection is not { } selection)
        {
            return false;
        }

        if (_resolveAsync is null)
        {
            return true;
        }

        try
        {
            var key = await _resolveAsync(selection, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (key is null)
            {
                Selection = null;
                IsResolving = false;
                SetResolutionError("That match is no longer in the current capture.");
                return false;
            }

            Selection = selection with { Key = key };
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Selection = null;
            IsResolving = false;
            throw;
        }
        catch
        {
            Selection = null;
            IsResolving = false;
            SetResolutionError("Could not resolve that match. Try again.");
            return false;
        }
    }

    private void SetResolutionError(string? message)
    {
        if (string.Equals(_resolutionError, message, StringComparison.Ordinal))
        {
            return;
        }

        _resolutionError = message;
        Raise(nameof(ValidationMessage));
    }

    /// <summary>Closes the prompt because the question it asks no longer has an answer.</summary>
    public void RequestClose(string reason) => CloseRequested?.Invoke(reason);

    private bool Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
